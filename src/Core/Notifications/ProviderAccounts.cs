using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Core.Notifications;

/// <summary>The platform's account for one channel, as the sender needs it (Amendment 01 §2.6).</summary>
public sealed record PlatformProvider(string ProviderCode, string SenderId, string CredentialRef);

public interface IPlatformProviderAccounts
{
    /// <summary>The platform's active account for the channel, or null when none is seeded or it is disabled.</summary>
    Task<PlatformProvider?> ForAsync(NotificationChannel channel, CancellationToken cancellationToken);
}

/// <summary>How long a resolved platform account is held before it is read again.</summary>
public sealed class PlatformProviderOptions
{
    public TimeSpan CacheFor { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Reads the platform's row of <c>provider_accounts</c> — <c>tenant_id IS NULL</c> (Amendment 01 §2.6) — as
/// <c>app_user</c>, in a read-only unit of work with no user and no tenant.
/// <para>
/// <b>Why that context, and why it is allowed:</b> §3 grants <c>SELECT</c> on this table to <c>app_user</c> alone;
/// <c>authenticator</c> has nothing on it, so the <c>forgot</c> unit of work (§5.1) cannot read it itself. The
/// <c>provider_accounts_read</c> policy of §4.1 is <c>tenant_id IS NULL OR tenant_id = app.tenant_id</c>, and its
/// first arm is true with no <c>app.tenant_id</c> set at all — proven on a real engine in
/// <c>docs/proposals/a3-forgot-pg-proof</c> (case 3b). So the platform row is reachable exactly as the policy
/// writes it, with no tenant and no user, and nothing of any tenant's is reachable in the same transaction.
/// The amendment does not say which role resolves the provider for a sessionless send; this is the PR's reading
/// of §2.6 + §3, and it is recorded for the project owner (OPEN_ITEMS 58).
/// </para>
/// <para>
/// The result is held for <see cref="PlatformProviderOptions.CacheFor"/>: this is platform configuration that
/// changes when migrator seeds it, not per-request data, and the <c>forgot</c> path must not pay a database read
/// it does not need (OPEN_ITEMS 51 — the shorter the work, the better the constant-time floor holds).
/// </para>
/// </summary>
public sealed class PlatformProviderAccounts(IServiceScopeFactory scopes, IOptions<PlatformProviderOptions> options)
    : IPlatformProviderAccounts
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<NotificationChannel, (PlatformProvider? Value, DateTime ReadAt)> cache = new();

    public async Task<PlatformProvider?> ForAsync(NotificationChannel channel, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.TryGetValue(channel, out var held) && now - held.ReadAt < options.Value.CacheFor)
                return held.Value;

            var code = channel.Code();
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
            // No user, no tenant: the policy's `tenant_id IS NULL` arm is what admits the row.
            var row = await UnitOfWork.RunReadOnlyAsync(db, SessionContext.None, (c, ct) =>
                c.ProviderAccounts
                    .Where(a => a.TenantId == null && a.Channel == code && a.IsActive)
                    .Select(a => new PlatformProvider(a.ProviderCode, a.SenderId, a.CredentialRef))
                    .SingleOrDefaultAsync(ct), cancellationToken);

            cache[channel] = (row, now);
            return row;
        }
        finally
        {
            gate.Release();
        }
    }
}

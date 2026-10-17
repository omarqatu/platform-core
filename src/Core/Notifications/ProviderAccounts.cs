using Core.Data;
using Core.Identity;
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
/// <c>authenticator</c>, in a read-only unit of work.
/// <para>
/// <b>Why that role:</b> the one path that sends with no session (<c>forgot</c>, §5.1) is <c>authenticator</c>'s,
/// so §3 grants it <c>SELECT</c> on this table and §4.1 gives it <c>provider_accounts_authenticator_read</c>,
/// <c>USING (tenant_id IS NULL)</c> — the platform's row and no other. The grant covers the table and the policy
/// confines the rows, so a per-tenant row, if one is ever written, is out of this role's reach without anyone
/// having to revisit the grant. An earlier draft read the row as <c>app_user</c> with neither a user nor a
/// tenant set; the project owner rejected it — an application role running contextless on a sessionless path is
/// a surface no document describes, and it was a reading of §2.6 rather than its text. The grant above is that
/// text (§0 records the three v1.16 paragraphs it amends).
/// </para>
/// <para>
/// The per-tenant delivery job is the other reader and is not this class: it runs per tenant as <c>app_user</c>
/// (§2.6, 1.16 §8) under <c>provider_accounts_read</c>, and it has no table yet (§5.5's outbox).
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
            var db = scope.ServiceProvider.GetRequiredService<AuthenticatorDbContext>();
            // No session of any kind, as everything this role does: the policy admits the platform's row and
            // nothing else, so there is no tenant to set and none that could be read if there were.
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

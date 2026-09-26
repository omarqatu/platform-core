using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.Data;

/// <summary>A tenant the user is an active member of, as the tenant-selection path reads it (4.3).</summary>
public sealed record MemberTenant(Guid TenantId, string Name);

/// <summary>
/// One tenant's outcome in a fan-out: 'ok' with its value; 'not_permitted' when the member lacks the permission in
/// that tenant (6.4: the overall set is the sum of what is due in each tenant); 'failed' when its transaction threw —
/// declared, never thrown, so the other tenants still return (PROOF_SPEC T6.3).
/// </summary>
public sealed record TenantResult<T>(MemberTenant Tenant, string Status, T? Value);

/// <summary>
/// The cross-tenant fan-out (PLATFORM_CORE 6.1, 6.3, 6.4): for every tenant among the user's active memberships, an
/// independent, read-only transaction in that tenant's own context — app.tenant_id, and the second axis resolved for
/// the membership in that tenant, in the mandatory order (3.5/6), never a value carried across tenants. Each tenant
/// has its own context object and connection, so nothing of one tenant's transaction — a failure included — reaches
/// the next. At most <c>batchCap</c> tenant transactions are open at once: the tenants run in batches of that size,
/// one batch after another.
/// </summary>
public static class TenantFanOut
{
    public const int DefaultBatchCap = 10;

    /// <summary>The user's active memberships in active tenants — app.user_id alone, through membership_self (4.3).</summary>
    public static Task<List<MemberTenant>> MemberTenantsAsync(CoreDbContext db, Guid userId, CancellationToken ct) =>
        UnitOfWork.RunReadOnlyAsync(db, new SessionContext(userId, null), (c, t) => (
            from m in c.Memberships
            where m.UserId == userId && m.Status == "active"
            join tenant in c.Tenants on m.TenantId equals tenant.Id
            where tenant.Status == "active"
            orderby tenant.Id
            select new MemberTenant(tenant.Id, tenant.Name)).ToListAsync(t), ct);

    public static async Task<List<TenantResult<T>>> RunAsync<TContext, T>(
        IReadOnlyList<MemberTenant> tenants, int batchCap, Func<TContext> newContext, Guid userId,
        Func<TContext, MemberTenant, CancellationToken, Task<T>> work, ILogger logger, CancellationToken ct)
        where TContext : CoreDbContext
    {
        if (batchCap < 1)
            throw new ArgumentOutOfRangeException(nameof(batchCap), batchCap, "The tenant batch cap must be at least 1 (6.3).");
        var results = new List<TenantResult<T>>(tenants.Count);
        foreach (var batch in tenants.Chunk(batchCap))
            results.AddRange(await Task.WhenAll(batch.Select(tenant => OneAsync(tenant, newContext, userId, work, logger, ct))));
        return results;
    }

    private static async Task<TenantResult<T>> OneAsync<TContext, T>(MemberTenant tenant, Func<TContext> newContext, Guid userId,
        Func<TContext, MemberTenant, CancellationToken, Task<T>> work, ILogger logger, CancellationToken ct)
        where TContext : CoreDbContext
    {
        await using var db = newContext();
        try
        {
            var value = await UnitOfWork.RunReadOnlyAsync(db, new SessionContext(userId, tenant.TenantId), (c, t) => work(c, tenant, t), ct);
            return new TenantResult<T>(tenant, "ok", value);
        }
        catch (NotPermittedException)
        {
            return new TenantResult<T>(tenant, "not_permitted", default);
        }
        catch (Exception error) when (!ct.IsCancellationRequested)
        {
            logger.LogError(error, "Unified view: tenant {TenantId} failed; the other tenants are returned.", tenant.TenantId);
            return new TenantResult<T>(tenant, "failed", default);
        }
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.Data;

/// <summary>
/// The background-job layer (PLATFORM_CORE 8, PROOF_SPEC T7): the tenants come from the job_runner connection —
/// active ones only (tenants_for_jobs, 3.4) — and each tenant is processed in an independent app_user transaction
/// with the system context of 8/5: app.tenant_id, app.scope_all = true, app.can_manage_scope = false,
/// app.can_manage_members = false, and app.membership_id unset. One of the two declared scope_all contexts (3.5/6);
/// this file is the second-axis writer of the job layer (Check 9). The job sees every entity of its tenant and
/// manages no scope and no member. One tenant's failure is declared, and the others complete.
/// </summary>
public static class SystemContext
{
    public const string ActorType = "job";
    public const int DefaultBatchCap = 10;

    /// <summary>The active tenants, as job_runner reads them — its only read (3.8).</summary>
    public static Task<List<Guid>> ActiveTenantsAsync(CoreDbContext jobRunner, CancellationToken ct) =>
        UnitOfWork.RunAsync(jobRunner, SessionContext.None, (c, t) => c.Tenants.OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(t), ct);

    /// <summary>One tenant, in the system context: its own transaction, committed when the work completes.</summary>
    public static async Task<T> RunAsync<TContext, T>(TContext db, Guid tenantId, Func<TContext, CancellationToken, Task<T>> work,
        CancellationToken ct)
        where TContext : CoreDbContext
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // Each its own SET LOCAL (3.5/2); constant names, and a Guid in "D" format or a literal — nothing
            // caller-controlled reaches the text. app.membership_id is never set: there is no membership.
#pragma warning disable EF1003
            await db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = '" + tenantId.ToString("D") + "'", ct);
#pragma warning restore EF1003
            await db.Database.ExecuteSqlRawAsync("SET LOCAL app.scope_all = 'true'", ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL app.can_manage_scope = 'false'", ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL app.can_manage_members = 'false'", ct);
            db.Audit = new AuditContext(tenantId, null, ActorType, null);
            var result = await work(db, ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        finally
        {
            db.Audit = null;
        }
    }

    /// <summary>
    /// The fan-out (8/2-3): the tenants in bounded batches — at most <paramref name="batchCap"/> transactions open at
    /// once, one batch after another — each on its own context and connection.
    /// </summary>
    public static async Task<List<(Guid TenantId, string Status, T? Value)>> FanOutAsync<TContext, T>(
        IReadOnlyList<Guid> tenants, int batchCap, Func<TContext> newContext, Func<TContext, Guid, CancellationToken, Task<T>> work,
        ILogger logger, CancellationToken ct)
        where TContext : CoreDbContext
    {
        if (batchCap < 1)
            throw new ArgumentOutOfRangeException(nameof(batchCap), batchCap, "The tenant batch cap must be at least 1 (8).");
        var results = new List<(Guid, string, T?)>(tenants.Count);
        foreach (var batch in tenants.Chunk(batchCap))
            results.AddRange(await Task.WhenAll(batch.Select(async tenant =>
            {
                await using var db = newContext();
                try
                {
                    return (tenant, "ok", await RunAsync(db, tenant, (c, t) => work(c, tenant, t), ct));
                }
                catch (Exception error) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(error, "Job: tenant {TenantId} failed; the other tenants complete.", tenant);
                    return (tenant, "failed", default(T));
                }
            })));
        return results;
    }
}

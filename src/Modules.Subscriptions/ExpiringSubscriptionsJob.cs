using System.Text.Json;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

/// <summary>
/// The trial background job (PROOF_SPEC T7): per tenant, count the subscriptions ending within 30 days of the job's
/// date and write one audit line. It runs in the system context (SystemContext, §8/5): it sees every subscription of
/// its tenant, with no assignment — its scope is stated here, not inferred from a request (§8/5: logic decided by a
/// user's scope in a request is decided by full scope in a job).
/// </summary>
public static class ExpiringSubscriptionsJob
{
    public const string Action = "subscriptions.expiring_within_30_days";
    public const int Days = 30;

    public static async Task<long> RunForTenantAsync(SubscriptionsDbContext db, Guid tenantId, DateOnly asOf, Guid runId, CancellationToken ct)
    {
        var until = asOf.AddDays(Days);
        var expiring = await db.Subscriptions.LongCountAsync(s => s.EndsOn >= asOf && s.EndsOn <= until, ct);
        db.AuditLog.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            ActorId = null,
            ActorType = SystemContext.ActorType,
            Action = Action,
            EntityType = "tenants",
            EntityId = tenantId,
            NewValue = JsonSerializer.Serialize(new { run_id = runId, as_of = asOf, days = Days, count = expiring }),
            CreatedAt = DateTime.UtcNow,
        });
        await CriticalWrite.SaveAsync(db, "audit_log job line", ct);
        return expiring;
    }
}

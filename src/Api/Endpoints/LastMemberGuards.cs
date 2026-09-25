using Core;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

/// <summary>The change would leave the tenant with no active owner (item a) or no active 'all' membership (item g).</summary>
public sealed class LastMemberException(string code, Guid membershipId)
    : InvalidOperationException($"Refused ({code}): membership {membershipId} is the last one (PLATFORM_CORE 3.10, items a and g).")
{
    public string Code { get; } = code;
}

/// <summary>
/// Items a and g (PLATFORM_CORE v1.16 §3.10, PROOF_SPEC v1.3 T4): in the transaction of the change, lock the rows
/// ORDER BY id FOR UPDATE OF m in its own statement — through membership_lock for either manager, through
/// membership_self_leave for the actor's own row — then count in a separate statement, which under READ COMMITTED
/// takes a fresh snapshot after the lock is granted and sees the other transaction's committed change. The lock
/// query's own rows are never compared with the count: it is not re-evaluated after the wait. Never an advisory
/// lock; never a lock on membership_scope.
/// </summary>
public static class LastMemberGuards
{
    private const string Tenant = "(SELECT NULLIF(current_setting('app.tenant_id', true), '')::uuid)";

    private const string IsOwner =
        "EXISTS (SELECT 1 FROM membership_roles mr JOIN roles r ON r.id = mr.role_id " +
        "WHERE mr.membership_id = m.id AND r.code = 'owner' AND r.is_system)";

    private const string IsAll =
        "EXISTS (SELECT 1 FROM membership_scope s WHERE s.membership_id = m.id AND s.scope_mode = 'all')";

    /// <summary>Item a — a departure, removing an owner role, or disabling an owner.</summary>
    public static Task RequireAnotherOwnerAsync(CoreDbContext db, Guid membershipId, CancellationToken ct) =>
        RequireAnotherAsync(db, IsOwner, membershipId, "last_owner", ct);

    /// <summary>Item g — a downgrade to 'assigned', disabling an 'all' member, or an 'all' member's departure.</summary>
    public static Task RequireAnotherAllAsync(CoreDbContext db, Guid membershipId, CancellationToken ct) =>
        RequireAnotherAsync(db, IsAll, membershipId, "last_all_member", ct);

    private static async Task RequireAnotherAsync(CoreDbContext db, string condition, Guid membershipId, string code, CancellationToken ct)
    {
        // 1. The lock — its own statement, ORDER BY id. Its result is deliberately unused.
#pragma warning disable EF1002, EF1003 // constant SQL text: the condition is one of the two constants above
        await db.Database.SqlQueryRaw<Guid>(
            $"SELECT m.id AS \"Value\" FROM memberships m WHERE m.tenant_id = {Tenant} AND m.status = 'active' AND {condition} " +
            "ORDER BY m.id FOR UPDATE OF m").ToListAsync(ct);

        // 2. The count — a separate statement, a fresh snapshot: is the target one of them, and how many others remain.
        var count = await db.Database.SqlQueryRaw<GuardCount>(
            "SELECT count(*) FILTER (WHERE m.id = {0}) AS \"Target\", count(*) FILTER (WHERE m.id <> {0}) AS \"Others\" " +
            $"FROM memberships m WHERE m.tenant_id = {Tenant} AND m.status = 'active' AND {condition}", membershipId).SingleAsync(ct);
#pragma warning restore EF1002, EF1003
        if (count is { Target: > 0, Others: 0 })
            throw new LastMemberException(code, membershipId);
    }

    private sealed class GuardCount
    {
        public long Target { get; set; }
        public long Others { get; set; }
    }
}

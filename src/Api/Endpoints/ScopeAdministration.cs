using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

/// <summary>The application layer refused an action the caller holds no permission for (5).</summary>
public sealed class NotPermittedException(string permission)
    : InvalidOperationException($"The caller does not hold {permission} (PLATFORM_CORE 5).")
{
    public string Permission { get; } = permission;
}

/// <summary>
/// The two administrative surfaces of 4.8 — a member's mode and a member's assignment — in two layers:
/// <list type="number">
/// <item>The application (5): each operation first requires core.scope.manage, from the scope resolved on this
/// transaction (3.5/6), and refuses before any write reaches the database.</item>
/// <item>The database (4.8, 1.9): the write itself, under membership_scope_admin_update and the
/// scope_assignment policies — the second layer, which holds on its own when the first is bypassed: its
/// silent zero rows become an error through the rows-affected guard (3.5/5), and an insert fails on the
/// policy.</item>
/// </list>
/// The write steps are public so the white-box tests can reach the second layer with the first bypassed.
/// Each change is audited automatically, in the same transaction (7).
/// </summary>
public static class ScopeAdministration
{
    public const string ManagePermission = "core.scope.manage";

    /// <summary>The first layer: the resolved permission, checked before any write.</summary>
    public static ResolvedScope RequireManage(CoreDbContext db)
    {
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        return scope.CanManageScope ? scope : throw new NotPermittedException(ManagePermission);
    }

    public static async Task ChangeModeAsync(CoreDbContext db, SessionContext session, Guid membershipId, string scopeMode,
        CancellationToken ct)
    {
        RequireManage(db);
        await WriteModeAsync(db, session, membershipId, scopeMode, ct);
    }

    public static async Task SetAssignmentAsync(CoreDbContext db, SessionContext session, Guid membershipId, Guid scopeRefId,
        bool active, string? reason, string? assignmentRole, CancellationToken ct)
    {
        RequireManage(db);
        await WriteAssignmentAsync(db, session, membershipId, scopeRefId, active, reason, assignmentRole, ct);
    }

    /// <summary>
    /// The second layer alone: a critical write (3.5/5) — the policy's silent zero rows (another tenant, no
    /// core.scope.manage, or the actor's own membership, 4.8) become an explicit error. A tracked update, so the
    /// automatic audit records it (7).
    /// </summary>
    public static async Task WriteModeAsync(CoreDbContext db, SessionContext session, Guid membershipId, string scopeMode,
        CancellationToken ct)
    {
        var row = CriticalWrite.Require(
            await db.MembershipScopes.SingleOrDefaultAsync(s => s.MembershipId == membershipId, ct), "membership_scope.scope_mode");
        row.ScopeMode = scopeMode;
        await CriticalWrite.SaveAsync(db, "membership_scope.scope_mode", ct);
    }

    /// <summary>
    /// The second layer alone: one permanent row per (membership, entity), never deleted (4.8). An existing row
    /// is a critical update of `active` alone — the matrix grants UPDATE (active) and nothing else (3.8); a new
    /// row is an insert under scope_assignment_insert. Both tracked, so the automatic audit records them (7).
    /// </summary>
    public static async Task WriteAssignmentAsync(CoreDbContext db, SessionContext session, Guid membershipId, Guid scopeRefId,
        bool active, string? reason, string? assignmentRole, CancellationToken ct)
    {
        var row = await db.ScopeAssignments.SingleOrDefaultAsync(a => a.MembershipId == membershipId && a.ScopeRefId == scopeRefId, ct);
        if (row is not null)
        {
            row.Active = active;
        }
        else
        {
            db.ScopeAssignments.Add(new ScopeAssignment
            {
                Id = Guid.CreateVersion7(),
                TenantId = session.TenantId!.Value,
                MembershipId = membershipId,
                ScopeRefId = scopeRefId,
                AssignmentRole = assignmentRole ?? "contributor",
                Active = active,
                Reason = reason,
            });
        }
        await CriticalWrite.SaveAsync(db, "scope_assignments.active", ct);
    }
}

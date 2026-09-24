using System.Text.Json;
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
/// Each change writes its audit row in the same transaction (7).
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

    public static async Task ChangeModeAsync(CoreDbContext db, SessionContext session, string? ipAddress,
        Guid membershipId, string scopeMode, CancellationToken ct)
    {
        RequireManage(db);
        await WriteModeAsync(db, session, ipAddress, membershipId, scopeMode, ct);
    }

    public static async Task SetAssignmentAsync(CoreDbContext db, SessionContext session, string? ipAddress,
        Guid membershipId, Guid scopeRefId, bool active, string? reason, string? assignmentRole, CancellationToken ct)
    {
        RequireManage(db);
        await WriteAssignmentAsync(db, session, ipAddress, membershipId, scopeRefId, active, reason, assignmentRole, ct);
    }

    /// <summary>
    /// The second layer alone: a critical write (3.5/5) — the policy's silent zero rows (another tenant, no
    /// core.scope.manage, or the actor's own membership, 4.8) become an explicit error.
    /// </summary>
    public static async Task WriteModeAsync(CoreDbContext db, SessionContext session, string? ipAddress,
        Guid membershipId, string scopeMode, CancellationToken ct)
    {
        var rows = db.MembershipScopes.Where(s => s.MembershipId == membershipId);
        var old = await rows.Select(s => new { s.Id, s.ScopeMode }).SingleOrDefaultAsync(ct);
        await CriticalWrite.ExpectRowsAsync(
            rows.ExecuteUpdateAsync(set => set.SetProperty(s => s.ScopeMode, scopeMode), ct),
            1, "membership_scope.scope_mode");

        Audit(db, session, ipAddress, "scope.mode_changed", "membership_scope", old!.Id,
            new { membership_id = membershipId, scope_mode = old.ScopeMode },
            new { membership_id = membershipId, scope_mode = scopeMode });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The second layer alone: one permanent row per (membership, entity), never deleted (4.8). An existing row
    /// is a critical update of `active` alone — the matrix grants UPDATE (active) and nothing else (3.8) — with
    /// the reason for the change in the audit entry; a new row is an insert under scope_assignment_insert.
    /// </summary>
    public static async Task WriteAssignmentAsync(CoreDbContext db, SessionContext session, string? ipAddress,
        Guid membershipId, Guid scopeRefId, bool active, string? reason, string? assignmentRole, CancellationToken ct)
    {
        var rows = db.ScopeAssignments.Where(a => a.MembershipId == membershipId && a.ScopeRefId == scopeRefId);
        var old = await rows.Select(a => new { a.Id, a.Active }).SingleOrDefaultAsync(ct);
        Guid id;
        if (old is not null)
        {
            id = old.Id;
            await CriticalWrite.ExpectRowsAsync(
                rows.ExecuteUpdateAsync(set => set.SetProperty(a => a.Active, active), ct),
                1, "scope_assignments.active");
        }
        else
        {
            id = Guid.CreateVersion7();
            db.ScopeAssignments.Add(new ScopeAssignment
            {
                Id = id,
                TenantId = session.TenantId!.Value,
                MembershipId = membershipId,
                ScopeRefId = scopeRefId,
                AssignmentRole = assignmentRole ?? "contributor",
                Active = active,
                Reason = reason,
            });
        }

        Audit(db, session, ipAddress, old is null ? "scope.assignment_created" : "scope.assignment_changed",
            "scope_assignments", id,
            old is null ? null : new { membership_id = membershipId, scope_ref_id = scopeRefId, active = old.Active },
            new { membership_id = membershipId, scope_ref_id = scopeRefId, active, reason });
        await db.SaveChangesAsync(ct);
    }

    private static void Audit(CoreDbContext db, SessionContext session, string? ipAddress, string action,
        string entityType, Guid entityId, object? oldValue, object newValue) =>
        db.AuditLog.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            TenantId = session.TenantId!.Value,
            ActorId = session.UserId,
            ActorType = "user",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValue = oldValue is null ? null : JsonSerializer.Serialize(oldValue),
            NewValue = JsonSerializer.Serialize(newValue),
            IpAddress = ipAddress,
            CreatedAt = DateTime.UtcNow,
        });
}

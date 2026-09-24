using System.Text.Json;
using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record ScopeModeRequest(string? ScopeMode);
public sealed record AssignmentRequest(bool Active, string? Reason, string? AssignmentRole);

/// <summary>
/// The second axis at the API: the caller's own resolved scope, and the two administrative surfaces of 4.8 —
/// a member's mode and a member's assignments. Administration is decided by the database alone
/// (core.scope.manage, resolved per transaction): these endpoints check no permission themselves, and a
/// refusal beneath them surfaces loudly through the rows-affected guard or the policy's error.
/// </summary>
public static class ScopeEndpoints
{
    public static void MapScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var scope = app.MapGroup("").RequireAuthorization();

        // Resolved on this request's transaction, not read from the session (3.5/6).
        scope.MapGet("/me/scope", async (CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is not { } resolved)
                return NoActiveTenant();
            var assignments = await db.ScopeAssignments
                .Where(a => a.MembershipId == resolved.MembershipId && a.Active)
                .OrderBy(a => a.ScopeRefId)
                .Select(a => a.ScopeRefId)
                .ToListAsync(ct);
            return Results.Ok(new
            {
                scope_mode = resolved.ScopeAll ? "all" : "assigned",
                can_manage_scope = resolved.CanManageScope,
                assignments,
            });
        });

        // Changing a member's mode: a critical write (3.5/5) — the policy's silent zero rows (another tenant,
        // no core.scope.manage, or the actor's own membership, 4.8) become an explicit error here.
        scope.MapPut("/scope/memberships/{membershipId:guid}/mode", async (Guid membershipId, ScopeModeRequest body,
            HttpContext http, CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            if (body.ScopeMode is not ("all" or "assigned"))
                return Results.Json(new { error = "invalid_value" }, statusCode: StatusCodes.Status400BadRequest);

            var rows = db.MembershipScopes.Where(s => s.MembershipId == membershipId);
            var old = await rows.Select(s => new { s.Id, s.ScopeMode }).SingleOrDefaultAsync(ct);
            await CriticalWrite.ExpectRowsAsync(
                rows.ExecuteUpdateAsync(set => set.SetProperty(s => s.ScopeMode, body.ScopeMode), ct),
                1, "membership_scope.scope_mode");

            Audit(db, http, "scope.mode_changed", "membership_scope", old!.Id,
                new { membership_id = membershipId, scope_mode = old.ScopeMode },
                new { membership_id = membershipId, scope_mode = body.ScopeMode });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Assigning, disabling, or re-enabling one (membership, entity) row — one permanent row, never deleted
        // (4.8). An existing row is a critical update of `active` alone — the matrix grants UPDATE (active) and
        // nothing else (3.8) — with the reason for the change in the audit entry; a new row is an insert under
        // scope_assignment_insert, carrying its reason.
        scope.MapPut("/scope/memberships/{membershipId:guid}/assignments/{scopeRefId:guid}", async (Guid membershipId,
            Guid scopeRefId, AssignmentRequest body, HttpContext http, CoreDbContext db,
            ISessionContextAccessor session, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();

            var rows = db.ScopeAssignments.Where(a => a.MembershipId == membershipId && a.ScopeRefId == scopeRefId);
            var old = await rows.Select(a => new { a.Id, a.Active }).SingleOrDefaultAsync(ct);
            Guid id;
            if (old is not null)
            {
                id = old.Id;
                await CriticalWrite.ExpectRowsAsync(
                    rows.ExecuteUpdateAsync(set => set.SetProperty(a => a.Active, body.Active), ct),
                    1, "scope_assignments.active");
            }
            else
            {
                id = Guid.CreateVersion7();
                db.ScopeAssignments.Add(new ScopeAssignment
                {
                    Id = id,
                    TenantId = session.Current.TenantId!.Value,
                    MembershipId = membershipId,
                    ScopeRefId = scopeRefId,
                    AssignmentRole = body.AssignmentRole ?? "contributor",
                    Active = body.Active,
                    Reason = body.Reason,
                });
            }

            Audit(db, http, old is null ? "scope.assignment_created" : "scope.assignment_changed", "scope_assignments", id,
                old is null ? null : new { membership_id = membershipId, scope_ref_id = scopeRefId, active = old.Active },
                new { membership_id = membershipId, scope_ref_id = scopeRefId, active = body.Active, reason = body.Reason });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static IResult NoActiveTenant() =>
        Results.Json(new { error = "no_active_tenant" }, statusCode: StatusCodes.Status409Conflict);

    // The audit row is written in the same transaction as the change (7): the request's unit of work.
    private static void Audit(CoreDbContext db, HttpContext http, string action, string entityType, Guid entityId,
        object? oldValue, object newValue)
    {
        var session = http.RequestServices.GetRequiredService<ISessionContextAccessor>().Current;
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
            IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            CreatedAt = DateTime.UtcNow,
        });
    }
}

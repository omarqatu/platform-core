using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record ScopeModeRequest(string? ScopeMode);
public sealed record AssignmentRequest(bool Active, string? Reason, string? AssignmentRole);

/// <summary>
/// One assignment row of a membership. Withdrawn rows are carried too (<c>Active</c> false): §4.8 keeps one
/// permanent row per (membership, entity), disabled and reactivated, never deleted — so a screen that listed only
/// the active ones would restore visibility its user never saw the moment the mode went back to 'assigned'.
/// </summary>
public sealed record AssignmentItem(Guid ScopeRefId, string AssignmentRole, bool Active, string? Reason);

/// <summary>A colleague's second axis: the mode, and every assignment row behind it.</summary>
public sealed record MembershipScopeResponse(string ScopeMode, IReadOnlyList<AssignmentItem> Assignments);

/// <summary>
/// The second axis at the API: the caller's own resolved scope, and the two administrative surfaces of 4.8 —
/// a member's mode and a member's assignments — each enforced in two layers (ScopeAdministration): the
/// explicit permission first (5), then the database beneath it.
/// </summary>
public static class ScopeEndpoints
{
    public static void MapScopeEndpoints(this IEndpointRouteBuilder app)
    {
        var scope = app.MapGroup("").RequireAuthorization();

        // Resolved on this request's transaction, not read from the session (3.5/6). permissions: every code the
        // caller holds in the active tenant, read as Permissions.RequireAsync reads one, so the interface can hide what
        // the server would refuse (OPEN_ITEMS 77) — never in place of each endpoint's own check.
        scope.MapGet("/me/scope", async (CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is not { } resolved)
                return NoActiveTenant();
            var assignments = await db.ScopeAssignments
                .Where(a => a.MembershipId == resolved.MembershipId && a.Active)
                .OrderBy(a => a.ScopeRefId)
                .Select(a => a.ScopeRefId)
                .ToListAsync(ct);
            var permissions = await Permissions.HeldAsync(db, ct);
            return Results.Ok(new
            {
                scope_mode = resolved.ScopeAll ? "all" : "assigned",
                can_manage_scope = resolved.CanManageScope,
                assignments,
                permissions,
            });
        });

        // A colleague's mode and assignments — what the assignment screen reads before it writes (group ب,
        // OPEN_ITEMS 100). core.scope.manage alone, and not because the endpoint says so: membership_scope_read and
        // scope_assignment_read admit a colleague's row only under can_manage_scope, because §4.8/1.8 decided that
        // "someone who sees every file has no corresponding need to see who was assigned to what". A member manager
        // without it would read zero rows here, so the endpoint refuses rather than answer an empty mode.
        scope.MapGet("/scope/memberships/{membershipId:guid}", async (Guid membershipId, CoreDbContext db,
            CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            ScopeAdministration.RequireManage(db);

            var mode = await db.MembershipScopes.Where(s => s.MembershipId == membershipId)
                .Select(s => s.ScopeMode).SingleOrDefaultAsync(ct);
            // No row: the membership is another tenant's, or has none at all (OPEN_ITEMS 39's gap). Both are
            // invisible to this reader and answer alike, as a hidden client and an unknown uuid do.
            if (mode is null)
                return Results.Json(new { error = ApiErrorCodes.NotPermitted }, statusCode: StatusCodes.Status403Forbidden);

            var assignments = await db.ScopeAssignments.Where(a => a.MembershipId == membershipId)
                .OrderBy(a => a.ScopeRefId)
                .Select(a => new AssignmentItem(a.ScopeRefId, a.AssignmentRole, a.Active, a.Reason)).ToListAsync(ct);
            return Results.Ok(new MembershipScopeResponse(mode, assignments));
        });

        scope.MapPut("/scope/memberships/{membershipId:guid}/mode", async (Guid membershipId, ScopeModeRequest body,
            CoreDbContext db, ISessionContextAccessor session, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            if (body.ScopeMode is not ("all" or "assigned"))
                return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);

            await ScopeAdministration.ChangeModeAsync(db, session.Current, membershipId, body.ScopeMode, ct);
            return Results.NoContent();
        });

        scope.MapPut("/scope/memberships/{membershipId:guid}/assignments/{scopeRefId:guid}", async (Guid membershipId,
            Guid scopeRefId, AssignmentRequest body, CoreDbContext db,
            ISessionContextAccessor session, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();

            await ScopeAdministration.SetAssignmentAsync(db, session.Current, membershipId, scopeRefId, body.Active, body.Reason,
                body.AssignmentRole, ct);
            return Results.NoContent();
        });
    }

    private static IResult NoActiveTenant() =>
        Results.Json(new { error = ApiErrorCodes.NoActiveTenant }, statusCode: StatusCodes.Status409Conflict);
}

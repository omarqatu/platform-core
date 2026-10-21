using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record ScopeModeRequest(string? ScopeMode);
public sealed record AssignmentRequest(bool Active, string? Reason, string? AssignmentRole);

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

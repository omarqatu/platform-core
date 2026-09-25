using Core;
using Core.Data;

namespace Api.Endpoints;

public sealed record MemberStatusRequest(string? Status);
public sealed record MemberRoleRequest(Guid? RoleId);
public sealed record InvitationRequest(string? Email, Guid? RoleId, string? IntendedScopeMode);

/// <summary>
/// Managing members at the API (PLATFORM_CORE v1.16 §3.10, §4.5): each manager endpoint goes through
/// MemberAdministration, which checks core.members.manage before the database (5, PROOF_SPEC T4.13); departure is
/// the member's own. All in the active tenant, on the request's transaction.
/// </summary>
public static class MemberEndpoints
{
    private const int MaxEmail = 256;

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("").RequireAuthorization();

        members.MapPut("/members/{membershipId:guid}/status", async (Guid membershipId, MemberStatusRequest body, CoreDbContext db,
            CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            if (body.Status is not ("active" or "disabled"))
                return InvalidValue();
            await MemberAdministration.SetStatusAsync(db, membershipId, body.Status, ct);
            return Results.NoContent();
        });

        members.MapPost("/members/{membershipId:guid}/roles", async (Guid membershipId, MemberRoleRequest body, CoreDbContext db,
            ISessionContextAccessor session, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            if (body.RoleId is not { } roleId)
                return InvalidValue();
            await MemberAdministration.AddRoleAsync(db, session.Current, membershipId, roleId, ct);
            return Results.NoContent();
        });

        members.MapDelete("/members/{membershipId:guid}/roles/{roleId:guid}", async (Guid membershipId, Guid roleId, CoreDbContext db,
            CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            await MemberAdministration.RemoveRoleAsync(db, membershipId, roleId, ct);
            return Results.NoContent();
        });

        // The response is the same whether a person with the email exists or not (Test 11): nothing is looked up.
        // The token is returned to the inviter — the proof sends no message (declared in the PR).
        members.MapPost("/members/invitations", async (InvitationRequest body, CoreDbContext db, ISessionContextAccessor session,
            CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            if (body is not { Email: { Length: > 0 and <= MaxEmail } email, RoleId: { } roleId, IntendedScopeMode: "all" or "assigned" })
                return InvalidValue();
            var created = await MemberAdministration.InviteAsync(db, session.Current, email, roleId, body.IntendedScopeMode, ct);
            return Results.Json(new { invitation_id = created.InvitationId, token = created.Token, expires_at = created.ExpiresAt },
                statusCode: StatusCodes.Status201Created);
        });

        members.MapPost("/members/invitations/{invitationId:guid}/revoke", async (Guid invitationId, CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            await MemberAdministration.RevokeAsync(db, invitationId, ct);
            return Results.NoContent();
        });

        // Departure (D4): the caller's own membership in the active tenant.
        members.MapPost("/me/leave", async (CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            await MemberAdministration.LeaveAsync(db, ct);
            return Results.NoContent();
        });
    }

    private static IResult NoActiveTenant() =>
        Results.Json(new { error = "no_active_tenant" }, statusCode: StatusCodes.Status409Conflict);

    private static IResult InvalidValue() =>
        Results.Json(new { error = "invalid_value" }, statusCode: StatusCodes.Status400BadRequest);
}

using Core;
using Core.Data;
using Core.Identity;
using Core.Notifications;
using Core.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Endpoints;

public sealed record MemberStatusRequest(string? Status);
public sealed record MemberRoleRequest(Guid? RoleId);
public sealed record InvitationRequest(string? Email, Guid? RoleId, string? IntendedScopeMode);

/// <summary>A role of the active tenant, named from the row the way a category is (name_ar/name_en are data).</summary>
public sealed record RoleItem(Guid Id, string Code, string NameAr, string NameEn, bool IsSystem);
public sealed record RolesResponse(IReadOnlyList<RoleItem> Items);

/// <summary>
/// A membership as the member screens show it. <c>ScopeMode</c> is null where <c>membership_scope_read</c> admits
/// no row — a caller who does not manage scope reads NULL for every colleague (4.8, 1.8) and a value for their own
/// row. Nothing here computes it; it is what the database returned. <c>IsSelf</c> is the actor's own row, which the
/// write policies refuse for the actor's own membership (§4.8, §3.10), so the screen states it instead of
/// offering actions that cannot work.
/// </summary>
public sealed record MemberItem(Guid MembershipId, string FullName, string Email, string Username, string Status,
    IReadOnlyList<RoleItem> Roles, string? ScopeMode, bool IsSelf);

/// <summary>
/// An invitation as the member screens show it. The token is absent by construction: it is returned once, to its
/// creator, at POST /members/invitations (DEVELOPER_GUIDE §9), and no list ever carries it or its hash.
/// </summary>
public sealed record InvitationItem(Guid Id, string Email, Guid RoleId, string IntendedScopeMode, string Status,
    DateTime ExpiresAt, DateTime CreatedAt);

/// <summary>
/// Managing members at the API (PLATFORM_CORE v1.16 §3.10, §4.5): each manager endpoint goes through
/// MemberAdministration, which checks core.members.manage before the database (5, PROOF_SPEC T4.13); departure is
/// the member's own. All in the active tenant, on the request's transaction.
/// </summary>
public static class MemberEndpoints
{
    private const int MaxEmail = 256;

    /// <summary>The forgot-password screen (web/src/screens/password/ForgotPasswordScreen.tsx), where recovery starts.</summary>
    public const string ForgotPath = "/app/password/forgot";

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var members = app.MapGroup("").RequireAuthorization();

        // ---- The read surfaces the member and scope screens need (group ب, OPEN_ITEMS 100). Each one is the same
        // two layers as the writes below: the explicit permission here (5), RLS beneath. No new grant and no new
        // policy — every SELECT they make was already in the §3.8 matrix, measured on PostgreSQL 18.6 before they
        // were written.

        // The tenant's role catalogue: the invitation form needs a role_id, and the role screen needs the names.
        // Whoever manages scope reads it too — assigning is impossible without knowing who holds what.
        members.MapGet("/roles", async (CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            MemberAdministration.RequireDirectory(db);
            var roles = await db.Roles.Where(r => r.IsActive).OrderBy(r => r.Code).ThenBy(r => r.Id)
                .Select(r => new RoleItem(r.Id, r.Code, r.NameAr, r.NameEn, r.IsSystem)).ToListAsync(ct);
            return Results.Ok(new RolesResponse(roles));
        });

        // Who is in the organization. The permission check is the whole of PROOF_SPEC T8.2 here: the database admits
        // every membership of the tenant to any member (membership_tenant_read), so an assigned member learns the
        // organization's size the moment this answers them anything at all — including an empty page or a count.
        // It must refuse, and the refusal carries no declaration (OPEN_ITEMS 103 asks whether the database should
        // hold this line itself; that is a schema change and the project owner's to decide).
        members.MapGet("/members", async (int? offset, int? limit, CoreDbContext db, ISessionContextAccessor session,
            CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            var scope = MemberAdministration.RequireDirectory(db);

            // The tenant_id constraint is NOT the isolation — RLS is, and it is intact. It is the list's subject.
            // Two permissive policies OR together on this table: membership_tenant_read (this tenant's memberships)
            // and membership_self (the caller's own, in EVERY tenant — which is how GET /tenants lists the
            // organizations a person belongs to, T3.7/OPEN_ITEMS 6). Without this line a person who is a member of
            // two organizations appears twice in each one's member list: their own row from the other tenant comes
            // through membership_self. Seen on the real seed, where Omar is in Al-Amin and in Maan.
            // LastMemberGuards does the same thing for the same reason, in SQL.
            var tenantId = session.Current.TenantId!.Value;

            // Ordered before any projection, with the id as the tiebreaker, so a page is stable (ScopedList).
            var ordered =
                from m in db.Memberships
                where m.TenantId == tenantId
                join u in db.Users on m.UserId equals u.Id
                join p in db.Persons on u.PersonId equals p.Id
                orderby p.FullName, m.Id
                select new MemberRow(m.Id, p.FullName, p.Email, u.Username, m.Status);
            var page = await ScopedList.PageAsync(scope, ordered, offset, limit, ct);

            var ids = page.Items.Select(m => m.MembershipId).ToList();
            var roles = await (
                from mr in db.MembershipRoles
                join r in db.Roles on mr.RoleId equals r.Id
                where ids.Contains(mr.MembershipId)
                orderby r.Code, r.Id
                select new { mr.MembershipId, Role = new RoleItem(r.Id, r.Code, r.NameAr, r.NameEn, r.IsSystem) }).ToListAsync(ct);
            // Only the rows membership_scope_read admits: absent is NULL below, never a guessed 'assigned'.
            var modes = await db.MembershipScopes.Where(s => ids.Contains(s.MembershipId))
                .Select(s => new { s.MembershipId, s.ScopeMode }).ToListAsync(ct);

            var items = page.Items.Select(m => new MemberItem(m.MembershipId, m.FullName, m.Email, m.Username, m.Status,
                roles.Where(r => r.MembershipId == m.MembershipId).Select(r => r.Role).ToList(),
                modes.SingleOrDefault(s => s.MembershipId == m.MembershipId)?.ScopeMode,
                m.MembershipId == scope.MembershipId)).ToList();
            return Results.Ok(new ScopedList<MemberItem>(page.ScopeMode, page.VisibleCount, page.TotalCount, page.HasMoreInScope, items));
        });

        // The pending invitations, so one can be revoked without its id being copied from a response days ago.
        // core.members.manage alone: a list of the addresses an organization has approached is a member-management
        // matter, and managing scope is not managing members (4.8 — the two axes do not borrow each other's rights).
        members.MapGet("/members/invitations", async (int? offset, int? limit, CoreDbContext db, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            var scope = MemberAdministration.RequireManage(db);
            var ordered = db.Invitations.OrderByDescending(i => i.CreatedAt).ThenBy(i => i.Id)
                .Select(i => new InvitationItem(i.Id, i.Email, i.RoleId, i.IntendedScopeMode, i.Status, i.ExpiresAt, i.CreatedAt));
            var page = await ScopedList.PageAsync(scope, ordered, offset, limit, ct);
            return Results.Ok(page);
        });

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

        // The response is the same whether a person with the email exists on the platform or not (Test 11): the one
        // lookup is this organization's own members (MemberAdministration.RequireNotAMemberAsync), which its caller
        // already reads at GET /members.
        // The token is returned to the inviter — the proof sends no message (declared in the PR).
        members.MapPost("/members/invitations", async (InvitationRequest body, CoreDbContext db, ISessionContextAccessor session,
            INotificationDispatch dispatch, IOptions<InvitationSettings> invitations, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            // One deliverable address, or nothing is written and nothing is queued (item 35): a name with no domain is
            // a row nobody can receive, and "a@x.com, b@y.com" is a link sent to two people — and one invitation
            // counted by the limit of OPEN_ITEMS 106.
            if (body is not { Email: { Length: > 0 and <= MaxEmail } email, RoleId: { } roleId, IntendedScopeMode: "all" or "assigned" }
                || !EmailAddress.IsSendable(email))
                return InvalidValue();
            var settings = invitations.Value;
            var created = await MemberAdministration.InviteAsync(db, session.Current, email, roleId, body.IntendedScopeMode, settings, ct);

            // The link goes out of this process and nowhere else (OPEN_ITEMS 60). Handed to the dispatcher, which
            // sends it outside the request, so the provider's round trip is not on this path; nothing here waits
            // on it, and nothing here logs the address or the body. Api refuses to start without an origin, so
            // TryOrigin cannot fail by this line.
            settings.TryOrigin(out var origin);
            var link = AcceptanceLink.For(origin, session.Current.TenantId!.Value, created.Token);
            dispatch.Enqueue(InvitationEmail.For(email, link, created.ExpiresAt));

            // The token is still returned to its creator, once, and the screen shows it once (OPEN_ITEMS 101):
            // the send is fire-and-forget and there is no way to re-issue an invitation yet, so removing it
            // would make a dropped message an invitation nobody can accept. It goes when re-issuing arrives.
            return Results.Json(new { invitation_id = created.InvitationId, token = created.Token, expires_at = created.ExpiresAt },
                statusCode: StatusCodes.Status201Created);
        });

        // Sign-in help (item 37): the member is sent their username and the forgot-password page, never a token.
        // Queued for the dispatcher like the invitation, so the provider's round trip is not on this path, and
        // nothing here logs the address or the body. Api refuses to start without an origin, so TryOrigin holds.
        members.MapPost("/members/{membershipId:guid}/sign-in-help", async (Guid membershipId, CoreDbContext db,
            ISessionContextAccessor session, SignInHelpLimiter limiter, INotificationDispatch dispatch,
            IOptions<InvitationSettings> invitations, CancellationToken ct) =>
        {
            if (db.Scope is null)
                return NoActiveTenant();
            var recipient = await MemberAdministration.SignInHelpAsync(db, session.Current, membershipId, limiter, ct);
            invitations.Value.TryOrigin(out var origin);
            dispatch.Enqueue(SignInHelpEmail.For(recipient.Email, recipient.Username, new Uri(origin, ForgotPath).ToString()));
            return Results.NoContent();
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

    /// <summary>The row the members list is ordered and paged on, before its roles and mode are attached.</summary>
    private sealed record MemberRow(Guid MembershipId, string FullName, string Email, string Username, string Status);

    private static IResult NoActiveTenant() =>
        Results.Json(new { error = ApiErrorCodes.NoActiveTenant }, statusCode: StatusCodes.Status409Conflict);

    private static IResult InvalidValue() =>
        Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
}

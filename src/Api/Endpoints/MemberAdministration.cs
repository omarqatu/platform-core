using Core;
using Core.Data;
using Core.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record CreatedInvitation(Guid InvitationId, string Token, DateTime ExpiresAt);

/// <summary>
/// Managing members (PLATFORM_CORE v1.16 §3.10, §4.5) in two layers, like scope administration:
/// <list type="number">
/// <item>The application (5): each manager operation first requires core.members.manage — CanManageMembers,
/// from the scope resolved on this transaction (3.5/6) — and refuses before any command reaches the database
/// (PROOF_SPEC T4.13).</item>
/// <item>The database (3.10): the write itself, under D3–D8 — the second layer, which holds on its own when the
/// first is bypassed: an update of a row no policy admits affects zero rows (→ the rows-affected guard, 3.5/5) or
/// fails with 42501; an insert fails on the policy.</item>
/// </list>
/// Departure is the member's own act (D4) and needs no permission. Items a and g guard every change that could
/// leave no active owner or no active 'all' membership (LastMemberGuards). Every write is a tracked write, audited
/// automatically in the same transaction (7). The write steps are public so the tests can reach the second layer
/// with the first bypassed.
/// </summary>
public static class MemberAdministration
{
    /// <summary>How long an invitation stays acceptable. Not set by the document; declared in the PR.</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    /// <summary>The first layer: the resolved permission, checked before any command.</summary>
    public static ResolvedScope RequireManage(CoreDbContext db)
    {
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        return scope.CanManageMembers ? scope : throw new NotPermittedException(ScopeResolver.ManageMembers);
    }

    public static async Task SetStatusAsync(CoreDbContext db, Guid membershipId, string status, CancellationToken ct)
    {
        RequireManage(db);
        await WriteStatusAsync(db, membershipId, status, ct);
    }

    public static async Task AddRoleAsync(CoreDbContext db, SessionContext session, Guid membershipId, Guid roleId, CancellationToken ct)
    {
        RequireManage(db);
        await WriteAddRoleAsync(db, session, membershipId, roleId, ct);
    }

    public static async Task RemoveRoleAsync(CoreDbContext db, Guid membershipId, Guid roleId, CancellationToken ct)
    {
        RequireManage(db);
        await WriteRemoveRoleAsync(db, membershipId, roleId, ct);
    }

    public static async Task<CreatedInvitation> InviteAsync(CoreDbContext db, SessionContext session, string email, Guid roleId,
        string intendedScopeMode, CancellationToken ct)
    {
        var scope = RequireManage(db);
        // An 'all' invitation also takes the scope permission (item f); the database refuses it on its own too.
        if (intendedScopeMode == "all" && !scope.CanManageScope)
            throw new NotPermittedException(ScopeResolver.ManageScope);
        return await WriteInvitationAsync(db, session, email, roleId, intendedScopeMode, ct);
    }

    public static async Task RevokeAsync(CoreDbContext db, Guid invitationId, CancellationToken ct)
    {
        RequireManage(db);
        await WriteRevokeAsync(db, invitationId, ct);
    }

    /// <summary>
    /// The second layer alone: another member between active and disabled (D3). Disabling an active owner takes
    /// item a; disabling an active 'all' member takes item g — which reads the target's scope row, visible only
    /// with the scope permission or for the actor's own row (membership_scope_read): unreadable → refused, loudly.
    /// </summary>
    public static async Task WriteStatusAsync(CoreDbContext db, Guid membershipId, string status, CancellationToken ct)
    {
        var membership = CriticalWrite.Require(await db.Memberships.SingleOrDefaultAsync(m => m.Id == membershipId, ct), "memberships.status");
        if (status == "disabled" && membership.Status == "active")
        {
            await LastMemberGuards.RequireAnotherOwnerAsync(db, membershipId, ct);
            var mode = await db.MembershipScopes.Where(s => s.MembershipId == membershipId).Select(s => s.ScopeMode).SingleOrDefaultAsync(ct)
                ?? throw new NotPermittedException(ScopeResolver.ManageScope);
            if (mode == "all")
                await LastMemberGuards.RequireAnotherAllAsync(db, membershipId, ct);
        }
        membership.Status = status;
        await CriticalWrite.SaveAsync(db, "memberships.status", ct);
    }

    /// <summary>The second layer alone: a role link for another member (D5: the permission, never one's own).</summary>
    public static async Task WriteAddRoleAsync(CoreDbContext db, SessionContext session, Guid membershipId, Guid roleId, CancellationToken ct)
    {
        db.MembershipRoles.Add(new MembershipRole
        {
            Id = Guid.CreateVersion7(), TenantId = session.TenantId!.Value, MembershipId = membershipId, RoleId = roleId,
        });
        await CriticalWrite.SaveAsync(db, "membership_roles insert", ct);
    }

    /// <summary>The second layer alone: removing a role link (D5); the owner role takes item a.</summary>
    public static async Task WriteRemoveRoleAsync(CoreDbContext db, Guid membershipId, Guid roleId, CancellationToken ct)
    {
        var link = CriticalWrite.Require(
            await db.MembershipRoles.SingleOrDefaultAsync(r => r.MembershipId == membershipId && r.RoleId == roleId, ct), "membership_roles delete");
        if (await db.Roles.AnyAsync(r => r.Id == roleId && r.Code == "owner" && r.IsSystem, ct))
            await LastMemberGuards.RequireAnotherOwnerAsync(db, membershipId, ct);
        db.MembershipRoles.Remove(link);
        await CriticalWrite.SaveAsync(db, "membership_roles delete", ct);
    }

    /// <summary>
    /// The second layer alone: an invitation (D6, 4.5/1) — created 'pending', invited_by the actor. Nothing about the
    /// email is looked up, so the response is the same whether a person with it exists or not (Test 11).
    /// </summary>
    public static async Task<CreatedInvitation> WriteInvitationAsync(CoreDbContext db, SessionContext session, string email, Guid roleId,
        string intendedScopeMode, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var token = InvitationToken.New();
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(), TenantId = session.TenantId!.Value, Email = email, RoleId = roleId,
            TokenHash = InvitationToken.Hash(token), Status = "pending", InvitedBy = session.UserId!.Value,
            ExpiresAt = now + InvitationLifetime, CreatedAt = now, IntendedScopeMode = intendedScopeMode,
        };
        db.Invitations.Add(invitation);
        await CriticalWrite.SaveAsync(db, "invitations insert", ct);
        return new CreatedInvitation(invitation.Id, token, invitation.ExpiresAt);
    }

    /// <summary>The second layer alone: pending → revoked only (D8), WHERE status = 'pending' (the concurrency token).</summary>
    public static async Task WriteRevokeAsync(CoreDbContext db, Guid invitationId, CancellationToken ct)
    {
        var invitation = CriticalWrite.Require(await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId, ct), "invitations.status revoked");
        invitation.Status = "revoked";
        await CriticalWrite.SaveAsync(db, "invitations.status revoked", ct);
    }

    /// <summary>
    /// A member departs (D4): their own membership, active → left. An owner takes item a; an 'all' member takes item g
    /// (decided by the project owner in T4: the last active 'all' membership cannot leave either). Item g needs the
    /// other members' scope rows, readable only with the scope permission (membership_scope_read), and their rows
    /// lockable only through membership_lock: a leaver without core.scope.manage would count and lock themselves
    /// alone — so they are refused, loudly, never guarded blind (OPEN_ITEMS 23).
    /// </summary>
    public static async Task LeaveAsync(CoreDbContext db, CancellationToken ct)
    {
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        var membership = CriticalWrite.Require(await db.Memberships.SingleOrDefaultAsync(m => m.Id == scope.MembershipId, ct), "memberships.status left");
        await LastMemberGuards.RequireAnotherOwnerAsync(db, membership.Id, ct);
        if (scope.ScopeAll)
        {
            if (!scope.CanManageScope)
                throw new NotPermittedException(ScopeResolver.ManageScope);
            await LastMemberGuards.RequireAnotherAllAsync(db, membership.Id, ct);
        }
        membership.Status = "left";
        await CriticalWrite.SaveAsync(db, "memberships.status left", ct);
    }
}

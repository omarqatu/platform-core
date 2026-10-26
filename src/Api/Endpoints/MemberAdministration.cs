using Core;
using Core.Data;
using Core.Identity;
using Core.Provisioning;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record CreatedInvitation(Guid InvitationId, string Token, DateTime ExpiresAt);

/// <summary>Too many invitations in the window, by this inviter or by this organization (OPEN_ITEMS 106).</summary>
public sealed class TooManyInvitationsException(int limit, TimeSpan window)
    : InvalidOperationException($"Refused: more than {limit} invitations in {window} (PLATFORM_CORE §4.5, OPEN_ITEMS 106).");

/// <summary>
/// The invitation is addressed to someone already active or disabled in this organization (the project owner's item
/// 35, group ج). Acceptance would refuse them already_member after the message had gone out; this refuses first.
/// </summary>
public sealed class AlreadyMemberException()
    : InvalidOperationException("Refused: the address belongs to an active or disabled member of this organization.");

/// <summary>Too many sign-in help messages in the window, to this member or from this organization (item 37).</summary>
public sealed class TooManySignInHelpException()
    : InvalidOperationException("Refused: the sign-in help limit for this member or this organization is reached (item 37).");

/// <summary>
/// The member's address cannot receive a message — written before item 35's rule existed. The same state answers
/// the same code everywhere: 400 invalid_value, as acceptance answers an invitation stored with such an address.
/// </summary>
public sealed class UndeliverableAddressException()
    : InvalidOperationException("Refused: the member's address is not one deliverable address (item 35).");

/// <summary>Who sign-in help goes to: the member's own address, and the username they sign in with.</summary>
public sealed record SignInHelpRecipient(string Email, string Username);

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
    /// <summary>How long an invitation stays acceptable: the one value both writers of an invitation use.</summary>
    public static readonly TimeSpan InvitationLifetime = InvitationToken.Lifetime;

    /// <summary>The first layer: the resolved permission, checked before any command.</summary>
    public static ResolvedScope RequireManage(CoreDbContext db)
    {
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        return scope.CanManageMembers ? scope : throw new NotPermittedException(ScopeResolver.ManageMembers);
    }

    /// <summary>
    /// The first layer for the two directory reads — the member list and the role catalogue (group ب): either
    /// management permission admits them, because whoever assigns clients must see whom they are assigning to, and
    /// the role catalogue is what an assignment screen names its people by (the project owner's decision).
    /// <para>
    /// This is where PROOF_SPEC T8.2 is held for member data, and it holds alone: <c>membership_tenant_read</c>
    /// admits every membership of the tenant to any member of it, so nothing beneath this line keeps an assigned
    /// member from counting their colleagues. Measured on PostgreSQL 18.6, not assumed — and recorded as
    /// OPEN_ITEMS 103, which asks whether the database should hold it instead.
    /// </para>
    /// </summary>
    public static ResolvedScope RequireDirectory(CoreDbContext db)
    {
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        return scope.CanManageMembers || scope.CanManageScope
            ? scope
            : throw new NotPermittedException(ScopeResolver.ManageMembers);
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
        string intendedScopeMode, InvitationSettings limits, CancellationToken ct)
    {
        var scope = RequireManage(db);
        // An 'all' invitation also takes the scope permission (item f); the database refuses it on its own too.
        if (intendedScopeMode == "all" && !scope.CanManageScope)
            throw new NotPermittedException(ScopeResolver.ManageScope);
        await RequireNotAMemberAsync(db, session, email, ct);
        await RequireUnderLimitsAsync(db, session, limits, ct);
        return await WriteInvitationAsync(db, session, email, roleId, intendedScopeMode, ct);
    }

    /// <summary>
    /// The two invitation limits (OPEN_ITEMS 106), on this request's own transaction, before the write.
    /// <para>
    /// Until invitations were delivered by e-mail this endpoint sent nothing and needed no limit; the PR that
    /// made it send is the one that closed the hole it opened. A holder of <c>core.members.manage</c> would
    /// otherwise be able to send unlimited mail to addresses of their choosing, through the platform's own
    /// sending account.
    /// </para>
    /// <para>
    /// Both counts read <c>invitations</c>, which needs no new grant and no new table — and note the difference
    /// between them. The per-inviter count names <c>invited_by</c> because that is **which rows it is about**.
    /// The tenant-wide count names no tenant at all: <c>invitations_tenant_read</c> is
    /// <c>tenant_id = app.tenant_id</c> with no self branch, so RLS has already scoped it, and adding a predicate
    /// would be the code filtering what the database filters (DEVELOPER_GUIDE §6).
    /// </para>
    /// <para>
    /// Every invitation created in the window counts, whatever became of it: an accepted or withdrawn one still
    /// sent a message. The answer is <c>429 too_many_attempts</c>, the code the login limiter already uses.
    /// </para>
    /// </summary>
    public static async Task RequireUnderLimitsAsync(CoreDbContext db, SessionContext session, InvitationSettings limits,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var byThisInviter = await db.Invitations
            .CountAsync(i => i.InvitedBy == session.UserId && i.CreatedAt > now - limits.PerInviterWindow, ct);
        if (byThisInviter >= limits.PerInviter)
            throw new TooManyInvitationsException(limits.PerInviter, limits.PerInviterWindow);

        var byThisTenant = await db.Invitations.CountAsync(i => i.CreatedAt > now - limits.PerTenantWindow, ct);
        if (byThisTenant >= limits.PerTenant)
            throw new TooManyInvitationsException(limits.PerTenant, limits.PerTenantWindow);
    }

    /// <summary>
    /// An invitation to someone who is already here, refused at creation rather than left to collide at acceptance
    /// (item 35). Active or disabled only: a member who <b>left</b> returns by an invitation (§3.10 D9), and a disabled
    /// one is enabled instead.
    /// <para>
    /// <b>Test 11 is untouched:</b> the read is this organization's memberships, never the platform's people. The
    /// address is compared under <see cref="EmailAddress"/>'s rule, in persons_email_normalized_key's own expression
    /// written out, as acceptance compares it (LINQ's <c>Trim()</c> is another rule). Nothing here reveals anything to
    /// its caller: holding <c>core.members.manage</c>, they already read every member's address at <c>GET /members</c>.
    /// </para>
    /// <para>
    /// <c>m.TenantId == tenantId</c> is the question's <b>subject</b>, not the isolation — RLS is the isolation, and is
    /// intact. memberships carries two permissive SELECT policies: <c>membership_tenant_read</c> (this tenant's) and
    /// <c>membership_self</c> (the caller's own, in every tenant). Without the line, an inviter who is themselves a
    /// member elsewhere would be refused when inviting their own address here, matched by their membership in the
    /// other organization — the reason <c>GET /members</c> and LastMemberGuards carry the same line (DEVELOPER_GUIDE §6).
    /// </para>
    /// </summary>
    public static async Task RequireNotAMemberAsync(CoreDbContext db, SessionContext session, string email, CancellationToken ct)
    {
        var tenantId = session.TenantId!.Value;
        var address = EmailAddress.Normalize(email);
        var here = await (
            from p in db.Persons.FromSql($"SELECT * FROM persons WHERE lower(btrim(email)) = {address}")
            join u in db.Users on p.Id equals u.PersonId
            join m in db.Memberships on u.Id equals m.UserId
            where m.TenantId == tenantId && (m.Status == "active" || m.Status == "disabled")
            select m.Id).AnyAsync(ct);
        if (here)
            throw new AlreadyMemberException();
    }

    /// <summary>The audit action of sign-in help (item 37): the one audit line not produced by a tracked write.</summary>
    public const string SignInHelpAction = "sign_in_help_sent";

    /// <summary>
    /// Sign-in help (the project owner's item 37, group ج): a plain message to a member, pointing them at the
    /// forgot-password page. No token, no new permission, no write on <c>password_reset_tokens</c> — the member starts
    /// the recovery themselves (the precedent is the reasoning of the withdrawn <c>announce-migration</c>, Amendment
    /// 01 §6/7: a precedent, not a reference in force).
    /// <list type="number">
    /// <item><c>core.members.manage</c>, first (5).</item>
    /// <item>The target: an <b>active</b> membership of this organization, not the caller's own, whose user is active —
    /// otherwise <c>403 not_permitted</c>, the code <c>PUT /members/{id}/status</c> answers a membership its policy
    /// does not admit. The message would send anyone else to a page that cannot help them: forgot-password sends to
    /// an active user only. A deliberate reuse, declared in the PR: the screen offers the action on active members
    /// alone, so the code's other meaning never reaches anyone. <c>m.TenantId == tenantId</c> is the subject, as in
    /// <c>GET /members</c> (<c>membership_self</c> admits the caller's memberships elsewhere).</item>
    /// <item>The address: one deliverable address, or <c>400 invalid_value</c> (item 35).</item>
    /// <item>The two limits (<see cref="SignInHelpLimiter"/>), taken only now, so a refusal above consumes nothing.</item>
    /// <item>The audit line, <see cref="SignInHelpAction"/>, on the request's transaction (the project owner's
    /// decision): the manager is the actor, the membership the entity, and the payload names the channel and nothing
    /// else — no address, no username. A critical write: zero rows is an error, and nothing is sent without it.</item>
    /// </list>
    /// The caller queues the message after this returns; the send is the dispatcher's, off the request's path.
    /// </summary>
    public static async Task<SignInHelpRecipient> SignInHelpAsync(CoreDbContext db, SessionContext session, Guid membershipId,
        SignInHelpLimiter limiter, CancellationToken ct)
    {
        var scope = RequireManage(db);
        if (membershipId == scope.MembershipId)
            throw new NotPermittedException(ScopeResolver.ManageMembers);
        var tenantId = session.TenantId!.Value;
        var target = await (
            from m in db.Memberships
            where m.Id == membershipId && m.TenantId == tenantId
            join u in db.Users on m.UserId equals u.Id
            join p in db.Persons on u.PersonId equals p.Id
            select new { MembershipStatus = m.Status, UserStatus = u.Status, p.Email, u.Username }).SingleOrDefaultAsync(ct);
        if (target is not { MembershipStatus: "active", UserStatus: "active" })
            throw new NotPermittedException(ScopeResolver.ManageMembers);
        if (!EmailAddress.IsSendable(target.Email))
            throw new UndeliverableAddressException();
        if (!limiter.TryTake(tenantId, membershipId, DateTime.UtcNow))
            throw new TooManySignInHelpException();

        var audit = db.Audit ?? throw new AuditContextMissingException("audit_log");
        db.AuditLog.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, ActorId = audit.ActorId, ActorType = audit.ActorType,
            Action = SignInHelpAction, EntityType = "memberships", EntityId = membershipId,
            OldValue = null, NewValue = """{"channel":"email"}""", IpAddress = audit.IpAddress, CreatedAt = DateTime.UtcNow,
        });
        await CriticalWrite.SaveAsync(db, "audit_log " + SignInHelpAction, ct);
        return new SignInHelpRecipient(target.Email.Trim(' '), target.Username);
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
    /// A member departs (D4): their own membership, active → left. An owner takes item a. An 'all' member holding a
    /// manager permission takes item g (decided by the project owner in T4): the last active 'all' membership among
    /// them cannot leave. A member holding neither core.scope.manage nor core.members.manage always leaves — consent
    /// prevails (0): item g does not reach them (OPEN_ITEMS 23). A manager holding core.members.manage alone cannot
    /// read the others' scope rows, so item g would count blind: refused loudly until OPEN_ITEMS 21 is settled.
    /// </summary>
    public static async Task LeaveAsync(CoreDbContext db, CancellationToken ct)
    {
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        var membership = CriticalWrite.Require(await db.Memberships.SingleOrDefaultAsync(m => m.Id == scope.MembershipId, ct), "memberships.status left");
        await LastMemberGuards.RequireAnotherOwnerAsync(db, membership.Id, ct);
        if (scope.ScopeAll && (scope.CanManageScope || scope.CanManageMembers))
        {
            if (!scope.CanManageScope)
                throw new NotPermittedException(ScopeResolver.ManageScope);
            await LastMemberGuards.RequireAnotherAllAsync(db, membership.Id, ct);
        }
        membership.Status = "left";
        await CriticalWrite.SaveAsync(db, "memberships.status left", ct);
    }
}

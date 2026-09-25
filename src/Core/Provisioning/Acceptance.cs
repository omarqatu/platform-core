using System.Security.Cryptography;
using System.Text;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Core.Provisioning;

/// <summary>The account an invitee with no account registers in the acceptance transaction (4.5/3).</summary>
public sealed record NewAccount(string Email, string FullName, string Username, string Password);

/// <summary>
/// An acceptance: the target tenant alongside the token (PROOF_SPEC T4.11), and — only for an invitee with no
/// account — the account to create. An invitee with an account is the authenticated user instead.
/// </summary>
public sealed record AcceptRequest(Guid TenantId, string Token, NewAccount? Account);

public sealed record AcceptResult(Guid MembershipId, Guid UserId, bool Returned);

/// <summary>An acceptance refused before anything was written; the transaction rolls back.</summary>
public sealed class InvitationRefusedException(string code, string reason)
    : InvalidOperationException($"Invitation refused ({code}): {reason}")
{
    public string Code { get; } = code;
}

/// <summary>The invitation token: random, sent to the invitee; only its hash is stored (4.5/2).</summary>
public static class InvitationToken
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// The acceptance path (4.4/3b, 4.5, 3.10), one provisioner transaction, audited in it (7):
/// <list type="number">
/// <item>app.tenant_id from the request, before the invitation is read (T4.11): a tenant id that is not the token's
/// → zero rows → a loud refusal.</item>
/// <item>The invitation: pending and unexpired; its email equal to the invitee's — the authenticated user's, or the
/// new account's — the token alone is not consent (4.5/3, T4.3).</item>
/// <item>No membership in the tenant → a new one: membership, its role, membership_scope in the invitation's mode
/// (item f), membership_auth 'password' (item c). A membership that left → it returns, in the binding order of
/// 3.10 (D9, D10). Active or disabled → refused.</item>
/// <item>Last, the invitation accepted WHERE status = 'pending': one row, or loud (single-use).</item>
/// </list>
/// The model maps no relationships, so EF orders no inserts: each layer is its own SaveChanges.
/// </summary>
public static class Acceptance
{
    public const string Provider = "password";

    public static Task<AcceptResult> RunAsync(ProvisionerDbContext db, AcceptRequest request, Guid? authenticatedUser,
        CancellationToken cancellationToken = default) =>
        ProvisionerUnitOfWork.RunAsync(db, (c, ct) => InTransactionAsync(c, request, authenticatedUser, ct), cancellationToken);

    /// <summary>The path itself, inside a transaction the caller opened (the white-box tests reach it this way).</summary>
    public static async Task<AcceptResult> InTransactionAsync(CoreDbContext db, AcceptRequest request, Guid? authenticatedUser,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if ((authenticatedUser is null) == (request.Account is null))
            throw new InvitationRefusedException("invalid_request", "either an authenticated user or a new account, not both");
        var userId = authenticatedUser ?? Guid.CreateVersion7();
        await ProvisionerUnitOfWork.EnterTenantAsync(db, request.TenantId, userId, "user", ct);

        var tokenHash = InvitationToken.Hash(request.Token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == tokenHash, ct)
            ?? throw new InvitationRefusedException("invalid_invitation", "no invitation for this token in this tenant");
        if (invitation.Status != "pending")
            throw new InvitationRefusedException("invalid_invitation", $"the invitation is {invitation.Status}");
        if (invitation.ExpiresAt <= now)
            throw new InvitationRefusedException("invitation_expired", "the invitation has expired");

        var email = authenticatedUser is { } user
            ? await (from u in db.Users where u.Id == user join p in db.Persons on u.PersonId equals p.Id select p.Email).SingleAsync(ct)
            : request.Account!.Email;
        if (!string.Equals(email.Trim(), invitation.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvitationRefusedException("email_mismatch", "the invitee's email is not the invitation's");

        var returned = false;
        Guid membershipId;
        if (authenticatedUser is null)
        {
            var account = request.Account!;
            if (await db.Persons.AnyAsync(p => p.Email == account.Email, ct) || await db.Users.AnyAsync(u => u.Username == account.Username, ct))
                throw new InvitationRefusedException("account_exists", "an account exists: log in, then accept");
            await Identities.CreateAsync(db, account.FullName, account.Email, account.Username, account.Password, now, ct, userId);
            membershipId = await JoinAsync(db, invitation, userId, now, ct);
        }
        else
        {
            var existing = await db.Memberships.SingleOrDefaultAsync(m => m.UserId == userId, ct);
            switch (existing?.Status)
            {
                case null:
                    membershipId = await JoinAsync(db, invitation, userId, now, ct);
                    break;
                case "left":
                    await ReturnAsync(db, existing, invitation, ct);
                    membershipId = existing.Id;
                    returned = true;
                    break;
                default:
                    throw new InvitationRefusedException("already_member", $"the membership in this tenant is {existing.Status}");
            }
        }

        // Single-use: WHERE status = 'pending' (the concurrency token) — one row, or loud.
        invitation.Status = "accepted";
        await CriticalWrite.SaveAsync(db, "invitations.status accepted", ct);
        return new AcceptResult(membershipId, userId, returned);
    }

    private static async Task<Guid> JoinAsync(CoreDbContext db, Invitation invitation, Guid userId, DateTime now, CancellationToken ct)
    {
        var membershipId = Guid.CreateVersion7();
        db.Memberships.Add(new Membership { Id = membershipId, TenantId = invitation.TenantId, UserId = userId, Status = "active", CreatedAt = now });
        await CriticalWrite.SaveAsync(db, "memberships", ct);
        MembershipParts.Add(db, invitation.TenantId, membershipId, invitation.RoleId, invitation.IntendedScopeMode);
        await CriticalWrite.SaveAsync(db, "membership parts", ct);
        return membershipId;
    }

    /// <summary>
    /// The return of a member who left (3.10, D9, D10) — the same row, in the binding order, each step its own
    /// SaveChanges while the row is still 'left' (the policies depend on it); re-activation last. membership_auth:
    /// the existing row is reused.
    /// </summary>
    public static async Task ReturnAsync(CoreDbContext db, Membership membership, Invitation invitation, CancellationToken ct)
    {
        // 1. delete the membership's role links (membership_roles_provisioner_delete)
        db.MembershipRoles.RemoveRange(await db.MembershipRoles.Where(r => r.MembershipId == membership.Id).ToListAsync(ct));
        await CriticalWrite.SaveAsync(db, "membership_roles of a returning member", ct);

        // 2. set its scope mode to the invitation's (membership_scope_provisioner_rejoin)
        var scope = await db.MembershipScopes.SingleOrDefaultAsync(s => s.MembershipId == membership.Id, ct)
            ?? throw new MissingMembershipScopeException(membership.Id);
        scope.ScopeMode = invitation.IntendedScopeMode;
        await CriticalWrite.SaveAsync(db, "membership_scope of a returning member", ct);

        // 3. disable its assignments (scope_assignments_provisioner_disable) — never deleted: history (4.8)
        foreach (var assignment in await db.ScopeAssignments.Where(a => a.MembershipId == membership.Id && a.Active).ToListAsync(ct))
            assignment.Active = false;
        await CriticalWrite.SaveAsync(db, "scope_assignments of a returning member", ct);

        // 4. insert the invitation's role (membership_roles_provisioner_insert)
        db.MembershipRoles.Add(new MembershipRole
        {
            Id = Guid.CreateVersion7(), TenantId = invitation.TenantId, MembershipId = membership.Id, RoleId = invitation.RoleId,
        });
        await CriticalWrite.SaveAsync(db, "membership_roles of a returning member", ct);

        // 5. re-activate: left → active (memberships_provisioner_rejoin) — last
        membership.Status = "active";
        await CriticalWrite.SaveAsync(db, "memberships.status returning", ct);
    }
}

/// <summary>A new membership's parts (4.4/3): its role, its scope row (never defaulted, 3.5/7), its provider.</summary>
internal static class MembershipParts
{
    public static void Add(CoreDbContext db, Guid tenantId, Guid membershipId, Guid roleId, string scopeMode)
    {
        db.MembershipRoles.Add(new MembershipRole { Id = Guid.CreateVersion7(), TenantId = tenantId, MembershipId = membershipId, RoleId = roleId });
        db.MembershipScopes.Add(new MembershipScope { Id = Guid.CreateVersion7(), TenantId = tenantId, MembershipId = membershipId, ScopeMode = scopeMode });
        db.MembershipAuths.Add(new MembershipAuth { Id = Guid.CreateVersion7(), TenantId = tenantId, MembershipId = membershipId, Provider = Acceptance.Provider });
    }
}

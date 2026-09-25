using Microsoft.EntityFrameworkCore;

namespace Core.Data;

/// <summary>The second-axis variables of one transaction, resolved from the database (PLATFORM_CORE §3.5/6).</summary>
public sealed record ResolvedScope(Guid MembershipId, bool ScopeAll, bool CanManageScope, bool CanManageMembers);

/// <summary>Rule 7 (§3.5/7): a membership with no membership_scope row is an error, never a silent default.</summary>
public sealed class MissingMembershipScopeException(Guid membershipId)
    : InvalidOperationException(
        $"Membership {membershipId} has no membership_scope row. A membership without a scope is invalid, not defaulted (PLATFORM_CORE 3.5/7).")
{
    public Guid MembershipId { get; } = membershipId;
}

/// <summary>The session's user has no active membership in the session's tenant: the tenant cannot be entered.</summary>
public sealed class NoActiveMembershipException(Guid userId, Guid tenantId)
    : InvalidOperationException($"User {userId} has no active membership in tenant {tenantId}.")
{
    public Guid UserId { get; } = userId;
    public Guid TenantId { get; } = tenantId;
}

/// <summary>
/// Second-axis resolution, on every transaction (§3.5/6): three separate reads in a mandatory order, each
/// reading only what the variable set before it allows — never one join, which returns zero before
/// app.membership_id is set (Test 26). Its source is the database alone: never a request, a header, a claim,
/// the session, or the token (Check 9). Runs inside a transaction where app.user_id and app.tenant_id are set.
/// </summary>
public static class ScopeResolver
{
    public const string ManageScope = "core.scope.manage";
    public const string ManageMembers = "core.members.manage";

    public static async Task<ResolvedScope> ResolveAsync(CoreDbContext db, Guid userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        // a. memberships, via membership_self (needs only app.user_id) → app.membership_id
        var membership = await db.Memberships
            .Where(m => m.UserId == userId && m.TenantId == tenantId && m.Status == "active")
            .Select(m => (Guid?)m.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NoActiveMembershipException(userId, tenantId);
        await SetLocalAsync(db, "SET LOCAL app.membership_id = '" + membership.ToString("D") + "'", cancellationToken);

        // b. membership_scope, via the self branch (needs only app.membership_id) → app.scope_all
        var scopeMode = RequireScopeRow(
            await db.MembershipScopes.Where(s => s.MembershipId == membership).Select(s => s.ScopeMode).SingleOrDefaultAsync(cancellationToken),
            membership);
        var scopeAll = scopeMode == "all";
        await SetLocalAsync(db, "SET LOCAL app.scope_all = '" + (scopeAll ? "true" : "false") + "'", cancellationToken);

        // c. membership_roles → role_permissions → permissions (the standard template + the global catalog):
        //    one read, then app.can_manage_scope and app.can_manage_members, in that order (3.5/6, 3.10).
        //    Permissions, not a scope: independent of the mode (§4.8, 1.9).
        var held = await (
            from mr in db.MembershipRoles
            join rp in db.RolePermissions on mr.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where mr.MembershipId == membership && (p.Code == ManageScope || p.Code == ManageMembers)
            select p.Code).Distinct().ToListAsync(cancellationToken);
        var canManageScope = held.Contains(ManageScope);
        var canManageMembers = held.Contains(ManageMembers);
        await SetLocalAsync(db, "SET LOCAL app.can_manage_scope = '" + (canManageScope ? "true" : "false") + "'", cancellationToken);
        await SetLocalAsync(db, "SET LOCAL app.can_manage_members = '" + (canManageMembers ? "true" : "false") + "'", cancellationToken);

        return new ResolvedScope(membership, scopeAll, canManageScope, canManageMembers);
    }

    /// <summary>Rule 7: the scope row must exist. Loud above; the policy beneath fails safe into zero rows.</summary>
    public static string RequireScopeRow(string? scopeMode, Guid membershipId) =>
        scopeMode ?? throw new MissingMembershipScopeException(membershipId);

    // SET LOCAL takes no bind parameters. Every statement above is built from a constant variable name and a
    // value that is either a Guid rendered in "D" format or the literal 'true'/'false' — nothing caller-controlled.
    private static Task SetLocalAsync(CoreDbContext db, string statement, CancellationToken ct) =>
#pragma warning disable EF1002
        db.Database.ExecuteSqlRawAsync(statement, ct);
#pragma warning restore EF1002
}

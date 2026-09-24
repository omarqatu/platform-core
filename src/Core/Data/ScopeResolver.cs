namespace Core.Data;

/// <summary>The second-axis variables of one transaction, resolved from the database (PLATFORM_CORE §3.5/6).</summary>
public sealed record ResolvedScope(Guid MembershipId, bool ScopeAll, bool CanManageScope);

/// <summary>Rule 7 (§3.5/7): a membership with no membership_scope row is an error, never a silent default.</summary>
public sealed class MissingMembershipScopeException(Guid membershipId)
    : InvalidOperationException(
        $"Membership {membershipId} has no membership_scope row. A membership without a scope is invalid, not defaulted (PLATFORM_CORE 3.5/7).")
{
    public Guid MembershipId { get; } = membershipId;
}

public static class ScopeResolver
{
    public static Task<ResolvedScope> ResolveAsync(CoreDbContext db, Guid userId, Guid tenantId, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("T3: second-axis resolution — Test 26 is written first.");

    /// <summary>Rule 7: the scope row must exist. Loud above; the policy beneath fails safe into zero rows.</summary>
    public static string RequireScopeRow(string? scopeMode, Guid membershipId) =>
        scopeMode ?? throw new MissingMembershipScopeException(membershipId);
}

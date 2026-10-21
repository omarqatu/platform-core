using Microsoft.EntityFrameworkCore;

namespace Core.Data;

/// <summary>The application layer refused an action the caller holds no permission for (5).</summary>
public sealed class NotPermittedException(string permission)
    : InvalidOperationException($"The caller does not hold {permission} (PLATFORM_CORE 5).")
{
    public string Permission { get; } = permission;
}

/// <summary>
/// The explicit permission at every endpoint (PLATFORM_CORE 5): a module's permission read on the request's own
/// transaction, from the caller's membership resolved on it (3.5/6) — never from the session. The two second-axis
/// permissions come from resolution itself (ResolvedScope); every other permission is read here, each time.
/// </summary>
public static class Permissions
{
    public static async Task<ResolvedScope> RequireAsync(CoreDbContext db, string permission, CancellationToken cancellationToken)
    {
        var scope = Resolved(db);
        var held = await HeldCodes(db, scope).AnyAsync(code => code == permission, cancellationToken);
        return held ? scope : throw new NotPermittedException(permission);
    }

    /// <summary>
    /// Every permission the caller holds in the active tenant, sorted (ordinal) — the same read as
    /// <see cref="RequireAsync"/>, on the caller's own transaction (it opens none). For the interface to hide what the
    /// server would refuse (GET /me/scope, OPEN_ITEMS 77): a courtesy, never a guard — each endpoint still requires
    /// its permission on its own request.
    /// </summary>
    public static async Task<IReadOnlyList<string>> HeldAsync(CoreDbContext db, CancellationToken cancellationToken)
    {
        var codes = await HeldCodes(db, Resolved(db)).Distinct().ToListAsync(cancellationToken);
        codes.Sort(StringComparer.Ordinal);
        return codes;
    }

    private static ResolvedScope Resolved(CoreDbContext db) =>
        db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");

    // The caller's membership → its roles → their permissions: the one read both of the above make.
    private static IQueryable<string> HeldCodes(CoreDbContext db, ResolvedScope scope) =>
        from mr in db.MembershipRoles
        join rp in db.RolePermissions on mr.RoleId equals rp.RoleId
        join p in db.Permissions on rp.PermissionId equals p.Id
        where mr.MembershipId == scope.MembershipId
        select p.Code;
}

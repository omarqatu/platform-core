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
        var scope = db.Scope ?? throw new InvalidOperationException("No scope is resolved: no active tenant.");
        var held = await (
            from mr in db.MembershipRoles
            join rp in db.RolePermissions on mr.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.Id
            where mr.MembershipId == scope.MembershipId && p.Code == permission
            select 1).AnyAsync(cancellationToken);
        return held ? scope : throw new NotPermittedException(permission);
    }
}

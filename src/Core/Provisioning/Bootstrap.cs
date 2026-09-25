using Core.Data;
using Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Core.Provisioning;

public sealed record BootstrapRequest(string TenantName, string FullName, string Email, string Username, string Password);

public sealed record BootstrapResult(Guid TenantId, Guid UserId, Guid MembershipId);

/// <summary>The role templates could not be read: bootstrap refuses loudly rather than create a tenant with no roles.</summary>
public sealed class RoleTemplatesUnavailableException(string what)
    : InvalidOperationException($"Bootstrap read {what}: the role templates are unavailable (PROOF_SPEC T4.9). Nothing was created.");

/// <summary>
/// The bootstrap path (4.4/3a, 3.9, PROOF_SPEC T4.8): a new tenant + its roles and role permissions from the
/// templates + the first person, user, credential and membership — the owner, scope 'all', provider 'password' —
/// in one transaction, audited in it (7). app.tenant_id is set to the new tenant before anything else.
/// The model maps no relationships, so EF orders no inserts: each layer is its own SaveChanges, parents first.
/// </summary>
public static class Bootstrap
{
    public const string OwnerTemplate = "owner";

    public static Task<BootstrapResult> RunAsync(ProvisionerDbContext db, BootstrapRequest request, CancellationToken cancellationToken = default) =>
        ProvisionerUnitOfWork.RunAsync(db, (c, ct) => InTransactionAsync(c, request, ct), cancellationToken);

    /// <summary>The path itself, inside a transaction the caller opened (the white-box tests reach it this way).</summary>
    public static async Task<BootstrapResult> InTransactionAsync(CoreDbContext db, BootstrapRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.CreateVersion7();
        await ProvisionerUnitOfWork.EnterTenantAsync(db, tenantId, null, "provisioner", ct);

        // The templates — read before any write. Zero rows is the policy's silent answer to a reader it does not
        // admit: loud here (T4.9), never a tenant with no roles.
        var templates = await db.RoleTemplates.AsNoTracking().OrderBy(t => t.Code).ToListAsync(ct);
        if (templates.Count == 0)
            throw new RoleTemplatesUnavailableException("zero role templates");
        var templatePermissions = await db.RoleTemplatePermissions.AsNoTracking().ToListAsync(ct);
        if (templatePermissions.Count == 0)
            throw new RoleTemplatesUnavailableException("zero role template permissions");
        if (templates.All(t => t.Code != OwnerTemplate))
            throw new RoleTemplatesUnavailableException("no owner template");

        db.Tenants.Add(new Tenant { Id = tenantId, Name = request.TenantName, Status = "active", CreatedAt = now });
        await CriticalWrite.SaveAsync(db, "tenants", ct);

        // Template-derived inserts are critical writes: rows written = templates (T4.9).
        var roles = templates.ToDictionary(t => t.Id, t => new Role
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn,
            IsSystem = true, IsActive = true,
        });
        db.Roles.AddRange(roles.Values);
        await CriticalWrite.ExpectRowsAsync(CriticalWrite.SaveAsync(db, "roles from templates", ct), templates.Count, "roles from templates");

        var rolePermissions = templatePermissions.Where(p => roles.ContainsKey(p.TemplateId)).Select(p => new RolePermission
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, RoleId = roles[p.TemplateId].Id, PermissionId = p.PermissionId,
        }).ToList();
        db.RolePermissions.AddRange(rolePermissions);
        await CriticalWrite.ExpectRowsAsync(CriticalWrite.SaveAsync(db, "role permissions from templates", ct), rolePermissions.Count,
            "role permissions from templates");

        var userId = await Identities.CreateAsync(db, request.FullName, request.Email, request.Username, request.Password, now, ct);

        var membershipId = Guid.CreateVersion7();
        db.Memberships.Add(new Membership { Id = membershipId, TenantId = tenantId, UserId = userId, Status = "active", CreatedAt = now });
        await CriticalWrite.SaveAsync(db, "memberships", ct);

        var ownerRole = roles[templates.Single(t => t.Code == OwnerTemplate).Id].Id;
        MembershipParts.Add(db, tenantId, membershipId, ownerRole, "all");
        await CriticalWrite.SaveAsync(db, "membership parts", ct);

        return new BootstrapResult(tenantId, userId, membershipId);
    }
}

/// <summary>The identity rows both provisioner paths create (4.1): person, user, and the password credential.</summary>
internal static class Identities
{
    public static async Task<Guid> CreateAsync(CoreDbContext db, string fullName, string email, string username, string password,
        DateTime now, CancellationToken ct, Guid? userId = null)
    {
        var personId = Guid.CreateVersion7();
        var user = userId ?? Guid.CreateVersion7();
        db.Persons.Add(new Person { Id = personId, FullName = fullName, Email = email, CreatedAt = now });
        await CriticalWrite.SaveAsync(db, "persons", ct);
        db.Users.Add(new User { Id = user, PersonId = personId, UserType = "employee", Username = username, Status = "active" });
        await CriticalWrite.SaveAsync(db, "users", ct);
        db.UserPasswordCredentials.Add(new UserPasswordCredential { UserId = user, PasswordHash = PasswordHashing.Hash(password), UpdatedAt = now });
        await CriticalWrite.SaveAsync(db, "user_password_credentials", ct);
        return user;
    }
}

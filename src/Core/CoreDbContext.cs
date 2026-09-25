using System.Text.RegularExpressions;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Core;

public class CoreDbContext : DbContext
{
    public CoreDbContext(DbContextOptions<CoreDbContext> options) : this((DbContextOptions)options)
    {
    }

    protected CoreDbContext(DbContextOptions options) : base(options)
    {
        // SaveChanges must not open a transaction of its own: outside the unit of work its
        // commands run with none, and the interceptor throws (3.5, T1.1).
        Database.AutoTransactionBehavior = AutoTransactionBehavior.Never;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPasswordCredential> UserPasswordCredentials => Set<UserPasswordCredential>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<MembershipRole> MembershipRoles => Set<MembershipRole>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<AuthAttempt> AuthAttempts => Set<AuthAttempt>();
    public DbSet<MembershipScope> MembershipScopes => Set<MembershipScope>();
    public DbSet<ScopeAssignment> ScopeAssignments => Set<ScopeAssignment>();
    public DbSet<MembershipAuth> MembershipAuths => Set<MembershipAuth>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RoleTemplate> RoleTemplates => Set<RoleTemplate>();
    public DbSet<RoleTemplatePermission> RoleTemplatePermissions => Set<RoleTemplatePermission>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    /// <summary>
    /// The second-axis values the unit of work resolved for the current transaction (3.5/6), or null
    /// outside one or when no tenant is active. Set by UnitOfWork only; never carried past the transaction.
    /// </summary>
    public ResolvedScope? Scope { get; internal set; }

    /// <summary>
    /// The tenant and actor this transaction's writes are audited under (7), or null outside one. Set by
    /// UnitOfWork and by the provisioner paths; never carried past the transaction.
    /// </summary>
    public AuditContext? Audit { get; internal set; }

    /// <summary>The caller's address, recorded on the audit entries (7). Set by the request pipeline.</summary>
    public string? ClientAddress { get; set; }

    // The automatic audit's state between SavingChanges and SavedChanges, and its recursion flag (7).
    internal List<AuditEntry>? PendingAudit { get; set; }
    internal bool SavingAudit { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Tenant>().ToTable("tenants");
        modelBuilder.Entity<Person>().ToTable("persons");
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<UserPasswordCredential>().ToTable("user_password_credentials").HasKey(x => x.UserId);
        modelBuilder.Entity<Membership>().ToTable("memberships");
        modelBuilder.Entity<MembershipRole>().ToTable("membership_roles");
        // Single-use (3.10): accepting or revoking updates WHERE status = the status read — 'pending' — so a
        // second acceptance or a revocation racing it writes zero rows, and EF's concurrency check makes that loud.
        modelBuilder.Entity<Invitation>(invitation =>
        {
            invitation.ToTable("invitations");
            invitation.Property(x => x.Status).IsConcurrencyToken();
        });
        modelBuilder.Entity<AuthAttempt>().ToTable("auth_attempts");
        modelBuilder.Entity<MembershipScope>().ToTable("membership_scope");
        modelBuilder.Entity<ScopeAssignment>().ToTable("scope_assignments");
        modelBuilder.Entity<MembershipAuth>().ToTable("membership_auth").Property(x => x.ProviderConfig).HasColumnType("jsonb");
        modelBuilder.Entity<Module>().ToTable("modules");
        modelBuilder.Entity<Permission>().ToTable("permissions");
        modelBuilder.Entity<TenantModule>().ToTable("tenant_modules");
        modelBuilder.Entity<Role>().ToTable("roles");
        modelBuilder.Entity<RolePermission>().ToTable("role_permissions");
        modelBuilder.Entity<RoleTemplate>().ToTable("role_templates");
        modelBuilder.Entity<RoleTemplatePermission>().ToTable("role_template_permissions").HasKey(x => new { x.TemplateId, x.PermissionId });
        modelBuilder.Entity<AuditEntry>(audit =>
        {
            audit.ToTable("audit_log");
            audit.Property(x => x.OldValue).HasColumnType("jsonb");
            audit.Property(x => x.NewValue).HasColumnType("jsonb");
        });

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        foreach (var property in entity.GetProperties())
        {
            property.SetColumnName(SnakeCase(property.Name));
            // Nothing generated by the database or by EF: no RETURNING in any command (§2, Test 28).
            property.ValueGenerated = ValueGenerated.Never;
        }
    }

    private static string SnakeCase(string name) => Regex.Replace(name, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();
}

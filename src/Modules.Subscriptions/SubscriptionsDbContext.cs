using Core;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

// The module's tables (PROOF_SPEC T5; schema in src/Migrations/Sql/0008_subscriptions.sql, and the classification and
// the subscription's own fields in 0012_classification.sql — Amendment 02 §3). Mapping only.

public sealed class Client
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>The client's own id: a client is its own scoped entity (the synonym, held by a CHECK).</summary>
    public Guid ScopeRefId { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class Subscription
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>The client this subscription belongs to — the composite FK (tenant_id, scope_ref_id) → clients.</summary>
    public Guid ScopeRefId { get; set; }
    public string ServiceName { get; set; } = "";
    public DateOnly EndsOn { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>What is being reminded about (Amendment 02 §2): one value today, <see cref="Subscription.Kind"/>.</summary>
    public string ReminderType { get; set; } = "";
    public Guid CategoryId { get; set; }
    public Guid? SubcategoryId { get; set; }
    public string? Provider { get; set; }
    /// <summary>Optional, and zero is a value (E5): a subscription that costs nothing still needs its reminder.</summary>
    public decimal? Cost { get; set; }
    public string? Currency { get; set; }
    /// <summary><c>monthly</c> or <c>yearly</c> — a closed catalog, held by a CHECK (§3.2).</summary>
    public string BillingCycle { get; set; } = "";
    public bool AutoRenew { get; set; }
    public string? LoginUrl { get; set; }

    public const string Kind = "subscription";
}

/// <summary>
/// The platform's classification (Amendment 02 §3.1): a global catalog with no tenant_id, seeded by migrator and read
/// by everyone (categories_read). No application role writes it.
/// </summary>
public sealed class Category
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>A category's optional detail (§3.1) — none is seeded yet. Read under subcategories_read.</summary>
public sealed class Subcategory
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public string Code { get; set; } = "";
    public string NameAr { get; set; } = "";
    public string NameEn { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// The module's context: the core's context and its transaction layer — the unit of work, second-axis resolution,
/// the automatic audit — plus the module's own tables. The core knows nothing of these (PLATFORM_CORE 9).
/// </summary>
public sealed class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options) : CoreDbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Subcategory> Subcategories => Set<Subcategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>().ToTable("clients");
        modelBuilder.Entity<Subscription>().ToTable("subscriptions");
        modelBuilder.Entity<Category>().ToTable("categories");
        modelBuilder.Entity<Subcategory>().ToTable("subcategories");
        // Last: the core's mapping names every column and marks every property ValueGenerated.Never (§2, Test 28).
        base.OnModelCreating(modelBuilder);
    }
}

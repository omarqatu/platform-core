using Core;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

// The module's tables (PROOF_SPEC T5; schema in src/Migrations/Sql/0008_subscriptions.sql). Mapping only.

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
}

/// <summary>
/// The module's context: the core's context and its transaction layer — the unit of work, second-axis resolution,
/// the automatic audit — plus the module's own tables. The core knows nothing of these (PLATFORM_CORE 9).
/// </summary>
public sealed class SubscriptionsDbContext(DbContextOptions<SubscriptionsDbContext> options) : CoreDbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Client>().ToTable("clients");
        modelBuilder.Entity<Subscription>().ToTable("subscriptions");
        // Last: the core's mapping names every column and marks every property ValueGenerated.Never (§2, Test 28).
        base.OnModelCreating(modelBuilder);
    }
}

using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Modules.Subscriptions;
using Npgsql;

namespace Core.WhiteBoxTests;

// PLATFORM_CORE v1.16 §3.7 Test 28 [W], (b) and (c), on the first module's EF model (PROOF_SPEC T5): the module's
// context extends the core's, so every property — the core's and the module's — is ValueGenerated.Never, and its
// inserts send no RETURNING. As migrator in a transaction that is rolled back.
[Collection(WhiteBoxCollection.Name)]
public class T5_ModuleModelTests(WhiteBoxFixture fixture)
{
    private static SubscriptionsDbContext Context(NpgsqlDataSource dataSource, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
    {
        var options = new DbContextOptionsBuilder<SubscriptionsDbContext>();
        options.UseCoreDataAccess(dataSource);
        if (extra.Length > 0)
            options.AddInterceptors(extra);
        return new SubscriptionsDbContext(options.Options);
    }

    [Fact]
    public async Task Test28b_ModuleModel_EveryPropertyIsValueGeneratedNever()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = Context(dataSource);

        var entities = db.Model.GetEntityTypes().ToList();
        Assert.Equal(21, entities.Count);
        Assert.Contains(entities, e => e.GetTableName() == "clients");
        Assert.Contains(entities, e => e.GetTableName() == "subscriptions");
        Assert.Empty(entities.SelectMany(e => e.GetProperties()).Where(p => p.ValueGenerated != ValueGenerated.Never));
    }

    [Fact]
    public async Task Test28c_ModuleInserts_SendNoReturning_AndAreAudited()
    {
        await using var dataSource = fixture.CreateMigratorDataSource();
        var recorder = new CommandRecorder();
        await using var db = Context(dataSource, recorder);
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Audit = new AuditContext(fixture.W1, null, "test", null);
        var client = Guid.CreateVersion7();

        db.Clients.Add(new Client { Id = client, TenantId = fixture.W1, ScopeRefId = client, Name = "wb", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.CreateVersion7(), TenantId = fixture.W1, ScopeRefId = client, ServiceName = "wb", EndsOn = new DateOnly(2027, 6, 1),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        var audited = await db.AuditLog.Where(a => a.TenantId == fixture.W1 && (a.EntityType == "clients" || a.EntityType == "subscriptions"))
            .Select(a => a.EntityType).OrderBy(t => t).ToListAsync();
        await transaction.RollbackAsync();

        Assert.Contains(recorder.Commands, c => c.Contains("INSERT INTO clients ", StringComparison.Ordinal));
        Assert.Contains(recorder.Commands, c => c.Contains("INSERT INTO subscriptions ", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("RETURNING", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(["clients", "subscriptions"], audited);
    }
}

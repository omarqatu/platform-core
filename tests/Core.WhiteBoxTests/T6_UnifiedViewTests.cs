using Core.Data;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.3 T6.5 [W] — the unified view is read-only: no write endpoint runs through it (its routes are GET
// only), and beneath the code each tenant transaction of the fan-out is READ ONLY, so a write inside one fails loudly
// (25006) and writes nothing. And the batch cap of 6.3: never more tenant transactions open at once than the cap.
[Collection(WhiteBoxCollection.Name)]
public class T6_UnifiedViewTests(WhiteBoxFixture fixture)
{
    [Fact]
    public Task T6_5_UnifiedRoutes_AreGetOnly() => InProcessApi.WithoutMigratorVariableAsync(async () =>
    {
        await using var api = InProcessApi.Create();
        var routes = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => (e.RoutePattern.RawText ?? "").TrimStart('/').StartsWith("unified", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(routes);
        Assert.All(routes, e => Assert.Equal(["GET"], e.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
    });

    [Fact]
    public async Task T6_5_AWriteInsideAFanOutTransaction_FailsLoudly_WritesNothing()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        var code = "t6.5-" + Guid.CreateVersion7().ToString("N")[..8];

        var results = await TenantFanOut.RunAsync([new MemberTenant(fixture.W1, "W1")], 10, () => WhiteBoxFixture.Context(dataSource),
            fixture.W1User, async (c, tenant, ct) =>
            {
                c.Roles.Add(new Role { Id = Guid.CreateVersion7(), TenantId = tenant.TenantId, Code = code, NameAr = "x", NameEn = "x", IsActive = true });
                return await c.SaveChangesAsync(ct);
            }, NullLogger.Instance, CancellationToken.None);

        Assert.Equal("failed", Assert.Single(results).Status);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE code = @c", ("c", code)));

        await using var db = WhiteBoxFixture.Context(dataSource);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => UnitOfWork.RunReadOnlyAsync(db, new SessionContext(fixture.W1User, fixture.W1),
            (c, ct) => c.Database.ExecuteSqlRawAsync("UPDATE roles SET name_en = name_en", ct)));
        Assert.Contains(Chain(error), e => e is PostgresException { SqlState: "25006" });
    }

    [Fact]
    public async Task TheBatchCap_BoundsTheOpenTenantTransactions()
    {
        await using var dataSource = fixture.CreateAppUserDataSource(maxPoolSize: 20);
        var tenants = Enumerable.Range(0, 7).Select(_ => new MemberTenant(fixture.W1, "W1")).ToList();
        var open = 0;
        var peak = 0;

        var results = await TenantFanOut.RunAsync(tenants, 3, () => WhiteBoxFixture.Context(dataSource), fixture.W1User,
            async (c, tenant, ct) =>
            {
                var now = Interlocked.Increment(ref open);
                lock (tenants) peak = Math.Max(peak, now);
                await Task.Delay(100, ct);
                var roles = await c.Roles.CountAsync(ct);
                Interlocked.Decrement(ref open);
                return roles;
            }, NullLogger.Instance, CancellationToken.None);

        Assert.Equal(7, results.Count);
        Assert.All(results, r => Assert.Equal("ok", r.Status));
        Assert.Equal(3, peak);
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}

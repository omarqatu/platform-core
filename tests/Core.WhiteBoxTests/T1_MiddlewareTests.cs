using System.Net.Http.Json;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC T1 build — the unit-of-work middleware: every request → a single
// explicit transaction. Driven through an in-memory TestServer; the endpoints
// below exist only in this test host, never in Api.
[Collection(T1ProbeCollection.Name)]
public class T1_MiddlewareTests(T1ProbeFixture probe)
{
    public sealed record Observed(string? UserId, string? TenantId, long FirstTxid, long SecondTxid, int VisibleRows);

    private sealed class FixedSession(SessionContext session) : ISessionContextAccessor
    {
        public SessionContext Current => session;
    }

    [Fact]
    public async Task Middleware_RunsTheWholeRequestInOneTransaction_WithTheSessionContext()
    {
        var userId = Guid.CreateVersion7();
        await using var dataSource = probe.CreateAppUserDataSource();
        using var host = await StartHostAsync(dataSource, new SessionContext(userId, probe.TenantA),
            async (http, db) =>
            {
                var observed = new Observed(
                    await ScalarAsync<string>(db, "SELECT current_setting('app.user_id', true) AS \"Value\""),
                    await ScalarAsync<string>(db, "SELECT current_setting('app.tenant_id', true) AS \"Value\""),
                    await ScalarAsync<long>(db, "SELECT txid_current() AS \"Value\""),
                    await ScalarAsync<long>(db, "SELECT txid_current() AS \"Value\""),
                    await db.Probes.CountAsync());
                await http.Response.WriteAsJsonAsync(observed);
            });

        var observed = await host.GetTestClient().GetFromJsonAsync<Observed>("/");

        Assert.NotNull(observed);
        Assert.Equal(userId.ToString(), observed.UserId);
        Assert.Equal(probe.TenantA.ToString(), observed.TenantId);
        Assert.Equal(observed.FirstTxid, observed.SecondTxid);
        Assert.Equal(T1ProbeFixture.TenantARows, observed.VisibleRows);
    }

    [Fact]
    public async Task Middleware_RollsBack_WhenTheRequestThrows()
    {
        const string label = "t1-middleware-rollback";
        await using var dataSource = probe.CreateAppUserDataSource();
        using var host = await StartHostAsync(dataSource, new SessionContext(null, probe.TenantA),
            async (_, db) =>
            {
                db.Probes.Add(new Probe { Id = Guid.CreateVersion7(), TenantId = probe.TenantA, Label = label });
                await db.SaveChangesAsync();
                throw new InvalidOperationException("fail after the write");
            });

        await Assert.ThrowsAnyAsync<Exception>(() => host.GetTestClient().GetAsync("/"));

        Assert.Equal(0, await probe.CountLabelAsMigratorAsync(label));
    }

    [Fact]
    public async Task Middleware_Commits_WhenTheRequestCompletes()
    {
        var label = "t1-middleware-commit-" + Guid.CreateVersion7();
        // A tenant of its own: a committed row must not change the counts the
        // other tests expect for tenants A and B.
        var tenant = Guid.CreateVersion7();
        await using var dataSource = probe.CreateAppUserDataSource();
        using var host = await StartHostAsync(dataSource, new SessionContext(null, tenant),
            async (http, db) =>
            {
                db.Probes.Add(new Probe { Id = Guid.CreateVersion7(), TenantId = tenant, Label = label });
                await db.SaveChangesAsync();
                http.Response.StatusCode = StatusCodes.Status204NoContent;
            });

        var response = await host.GetTestClient().GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await probe.CountLabelAsMigratorAsync(label));
    }

    private static async Task<IHost> StartHostAsync(
        NpgsqlDataSource dataSource, SessionContext session, Func<HttpContext, ProbeDbContext, Task> endpoint)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddDbContext<ProbeDbContext>(options => ProbeDbContext.Configure(options, dataSource));
                    // The middleware and the endpoint share one context, hence one transaction.
                    services.AddScoped<CoreDbContext>(sp => sp.GetRequiredService<ProbeDbContext>());
                    services.AddSingleton<ISessionContextAccessor>(new FixedSession(session));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<UnitOfWorkMiddleware>();
                    app.Run(http => endpoint(http, http.RequestServices.GetRequiredService<ProbeDbContext>()));
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private static Task<T> ScalarAsync<T>(ProbeDbContext db, string sql) =>
        db.Database.SqlQueryRaw<T>(sql).SingleAsync();
}

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

// PROOF_SPEC T1 build — the unit-of-work middleware: every request → a single explicit transaction.
// Driven through an in-memory TestServer; the endpoints below exist only in this test host, never in Api.
// On the real schema since T2: roles and audit_log of the fixture's tenant W1.
[Collection(WhiteBoxCollection.Name)]
public class T1_MiddlewareTests(WhiteBoxFixture fixture)
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
        await using var dataSource = fixture.CreateAppUserDataSource();
        using var host = await StartHostAsync(dataSource, new SessionContext(userId, fixture.W1),
            async (http, db) =>
            {
                var observed = new Observed(
                    await ScalarAsync<string>(db, "SELECT current_setting('app.user_id', true) AS \"Value\""),
                    await ScalarAsync<string>(db, "SELECT current_setting('app.tenant_id', true) AS \"Value\""),
                    await ScalarAsync<long>(db, "SELECT txid_current() AS \"Value\""),
                    await ScalarAsync<long>(db, "SELECT txid_current() AS \"Value\""),
                    await db.Roles.CountAsync());
                await http.Response.WriteAsJsonAsync(observed);
            });

        var observed = await host.GetTestClient().GetFromJsonAsync<Observed>("/");

        Assert.NotNull(observed);
        Assert.Equal(userId.ToString(), observed.UserId);
        Assert.Equal(fixture.W1.ToString(), observed.TenantId);
        Assert.Equal(observed.FirstTxid, observed.SecondTxid);
        Assert.Equal(WhiteBoxFixture.W1CustomRoles + 1, observed.VisibleRows);
    }

    [Fact]
    public async Task Middleware_RollsBack_WhenTheRequestThrows()
    {
        var action = "t1-middleware-rollback-" + Guid.CreateVersion7();
        await using var dataSource = fixture.CreateAppUserDataSource();
        using var host = await StartHostAsync(dataSource, new SessionContext(null, fixture.W1),
            async (_, db) =>
            {
                db.AuditLog.Add(Audit(fixture.W1, action));
                await db.SaveChangesAsync();
                throw new InvalidOperationException("fail after the write");
            });

        await Assert.ThrowsAnyAsync<Exception>(() => host.GetTestClient().GetAsync("/"));

        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE action = @a", ("a", action)));
    }

    [Fact]
    public async Task Middleware_Commits_WhenTheRequestCompletes()
    {
        var action = "t1-middleware-commit-" + Guid.CreateVersion7();
        await using var dataSource = fixture.CreateAppUserDataSource();
        using var host = await StartHostAsync(dataSource, new SessionContext(null, fixture.W1),
            async (http, db) =>
            {
                db.AuditLog.Add(Audit(fixture.W1, action));
                await db.SaveChangesAsync();
                http.Response.StatusCode = StatusCodes.Status204NoContent;
            });

        var response = await host.GetTestClient().GetAsync("/");

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE action = @a", ("a", action)));
    }

    private static AuditEntry Audit(Guid tenant, string action) => new()
    {
        Id = Guid.CreateVersion7(), TenantId = tenant, ActorType = "test", Action = action, EntityType = "test",
        CreatedAt = DateTime.UtcNow,
    };

    private static async Task<IHost> StartHostAsync(
        NpgsqlDataSource dataSource, SessionContext session, Func<HttpContext, Core.CoreDbContext, Task> endpoint)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    // The middleware and the endpoint share one context, hence one transaction.
                    services.AddDbContext<Core.CoreDbContext>(options => options.UseCoreDataAccess(dataSource));
                    services.AddSingleton<ISessionContextAccessor>(new FixedSession(session));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<UnitOfWorkMiddleware>();
                    app.Run(http => endpoint(http, http.RequestServices.GetRequiredService<Core.CoreDbContext>()));
                }))
            .Build();
        await host.StartAsync();
        return host;
    }

    private static Task<T> ScalarAsync<T>(Core.CoreDbContext db, string sql) =>
        db.Database.SqlQueryRaw<T>(sql).SingleAsync();
}

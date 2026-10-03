using System.Net;
using System.Net.Http.Json;
using Core;
using Core.Identity;
using Core.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Subscriptions;
using Npgsql;

namespace Core.WhiteBoxTests;

// CSRF (OPEN_ITEMS 28) [W] — a refused request reaches no database command: Api hosted in-process, the command
// recorder on every one of its contexts (app_user, the module's, authenticator, provisioner), as in T3/T4. Refused:
// a login with no Origin, a signed-in tenant selection from a sibling subdomain, an acceptance with no
// X-Requested-With — 403 and zero commands. The control first: a tenant selection with both headers runs its
// commands, so an empty recording means a refusal, not a recorder that sees nothing.
[Collection(WhiteBoxCollection.Name)]
public class CsrfNoDatabaseCommandTests(WhiteBoxFixture fixture)
{
    [Fact]
    public Task RefusedRequests_ReachNoDatabaseCommand() => InProcessApi.WithoutMigratorVariableAsync(async () =>
    {
        var recorder = new CommandRecorder();
        await using var api = InProcessApi.Create().WithWebHostBuilder(web => web.ConfigureServices(services =>
        {
            services.ConfigureDbContext<CoreDbContext>(o => o.AddInterceptors(recorder));
            services.ConfigureDbContext<SubscriptionsDbContext>(o => o.AddInterceptors(recorder));
            services.ConfigureDbContext<AuthenticatorDbContext>(o => o.AddInterceptors(recorder));
            services.ConfigureDbContext<ProvisionerDbContext>(o => o.AddInterceptors(recorder));
        }));
        // The harness's client (Origin and X-Requested-With on every request), cookies handled by hand as in T3.
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(InProcessApi.Origin),
            HandleCookies = false,
        });
        // A client of the same host with no default header at all: each refused request sets its own.
        using var bare = api.Server.CreateClient();

        // The control: with both headers, a tenant selection runs its commands.
        var login = await client.PostAsJsonAsync("/auth/login", Omar);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var session = Assert.Single(login.Headers.GetValues("Set-Cookie"), h => h.StartsWith("session=")).Split(';')[0];
        var tenant = await AlAminAsync();
        using (var select = Request(HttpMethod.Post, $"/tenants/{tenant}/select", session))
        {
            recorder.Clear();
            Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(select)).StatusCode);
            Assert.NotEmpty(recorder.Commands);
        }

        // Refused: each one 403, csrf_rejected, and not one command.
        foreach (var request in Refused(tenant, session))
        {
            recorder.Clear();
            var response = await bare.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(ApiErrorCodes.CsrfRejected, (await response.Content.ReadFromJsonAsync<ErrorBody>())!.Error);
            Assert.Empty(recorder.Commands);
        }
    });

    private static readonly object Omar = new { username = "omar", password = "omar-seed-password" };

    private static IEnumerable<HttpRequestMessage> Refused(Guid tenant, string session)
    {
        // A login with X-Requested-With and no Origin.
        var login = Request(HttpMethod.Post, "/auth/login");
        login.Content = JsonContent.Create(Omar);
        login.Headers.Add("X-Requested-With", "platform-web");
        yield return login;

        // A signed-in tenant selection from a sibling subdomain.
        var select = Request(HttpMethod.Post, $"/tenants/{tenant}/select", session);
        select.Headers.Add("Origin", "https://evil.localhost");
        select.Headers.Add("X-Requested-With", "platform-web");
        yield return select;

        // An acceptance from the allowed Origin, with no X-Requested-With.
        var accept = Request(HttpMethod.Post, "/invitations/accept");
        accept.Content = JsonContent.Create(new { tenant_id = tenant, token = "any" });
        accept.Headers.Add("Origin", InProcessApi.Origin);
        yield return accept;
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? session = null)
    {
        var request = new HttpRequestMessage(method, new Uri(new Uri(InProcessApi.Origin), path));
        if (session is not null)
            request.Headers.Add("Cookie", session);
        return request;
    }

    private async Task<Guid> AlAminAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id FROM tenants WHERE name = 'Al-Amin'", connection);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    private sealed record ErrorBody(string Error);
}

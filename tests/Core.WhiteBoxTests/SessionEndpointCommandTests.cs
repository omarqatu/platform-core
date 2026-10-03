using System.Net;
using System.Net.Http.Json;
using Core.Identity;
using Core.Provisioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Subscriptions;
using Npgsql;

namespace Core.WhiteBoxTests;

// The session endpoints of the identity screens [W] (approved by the project owner with them): GET /me runs on the
// tenant-selection path — app.user_id alone, even with a tenant in the cookie: no app.tenant_id, no second-axis
// variable (T3.7) — and POST /tenants/deselect and POST /auth/logout send no database command at all. Api hosted
// in-process, the command recorder on all four of its contexts.
[Collection(WhiteBoxCollection.Name)]
public class SessionEndpointCommandTests(WhiteBoxFixture fixture)
{
    [Fact]
    public Task Me_SetsUserIdAlone_EvenWithATenantSelected_DeselectAndLogout_SendNoCommand() => InProcessApi.WithoutMigratorVariableAsync(async () =>
    {
        var recorder = new CommandRecorder();
        await using var api = InProcessApi.Create().WithWebHostBuilder(web => web.ConfigureServices(services =>
        {
            services.ConfigureDbContext<CoreDbContext>(o => o.AddInterceptors(recorder));
            services.ConfigureDbContext<SubscriptionsDbContext>(o => o.AddInterceptors(recorder));
            services.ConfigureDbContext<AuthenticatorDbContext>(o => o.AddInterceptors(recorder));
            services.ConfigureDbContext<ProvisionerDbContext>(o => o.AddInterceptors(recorder));
        }));
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri(InProcessApi.Origin) });
        var omar = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'omar'");
        var alAmin = await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Al-Amin'");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { username = "omar", password = "omar-seed-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/tenants/{alAmin}/select", null)).StatusCode);

        recorder.Clear();
        var me = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal([$"SET LOCAL app.user_id = '{omar}'"], recorder.Commands.Where(c => c.Contains("SET LOCAL")).ToList());
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("app.tenant_id") || c.Contains("app.membership_id"));

        // Deselect: a new cookie with the user alone, and not one command.
        recorder.Clear();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/tenants/deselect", null)).StatusCode);
        Assert.Empty(recorder.Commands);
        var after = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/me");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, after.GetProperty("active_tenant").ValueKind);

        recorder.Clear();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Empty(recorder.Commands);
    });

    private async Task<Guid> ScalarGuidAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync())!;
    }
}

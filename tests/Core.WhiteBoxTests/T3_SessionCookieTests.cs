using System.Net;
using System.Net.Http.Json;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.2 T3.6 [W] — the session cookie carries user_id and the active tenant only: no scope, no
// permission. Api hosted in-process (its real Program, its real pipeline) against the seed contract; the
// cookie is decrypted with the Api's own ticket format and its content read claim by claim.
// And the tenant-selection path through Core's unit of work: app.user_id alone, no second-axis variable.
[Collection(WhiteBoxCollection.Name)]
public class T3_SessionCookieTests(WhiteBoxFixture fixture)
{
    [Fact]
    public async Task T3_6_Cookie_CarriesUserAndActiveTenantOnly()
    {
        await using var api = Api();
        using var client = api.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        var omar = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'omar'");
        var alAmin = await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Al-Amin'");

        var login = await client.PostAsJsonAsync("/auth/login", new { username = "omar", password = "omar-seed-password" });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var afterLogin = SessionCookieOf(login);

        using var select = new HttpRequestMessage(HttpMethod.Post, $"/tenants/{alAmin}/select");
        select.Headers.Add("Cookie", "session=" + afterLogin.Value);
        var selected = await client.SendAsync(select);
        Assert.Equal(HttpStatusCode.NoContent, selected.StatusCode);
        var afterSelect = SessionCookieOf(selected);

        foreach (var cookie in new[] { afterLogin, afterSelect })
        {
            Assert.Contains("httponly", cookie.Attributes);
            Assert.Contains("secure", cookie.Attributes);
            Assert.Contains("samesite=strict", cookie.Attributes);
        }

        var format = api.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(SessionCookie.Scheme).TicketDataFormat;
        var loginTicket = format.Unprotect(afterLogin.Value)!;
        var selectTicket = format.Unprotect(afterSelect.Value)!;

        // After login: the user alone. After selection: the user and the active tenant — nothing else.
        Assert.Equal([(SessionCookie.UserId, omar.ToString("D"))],
            loginTicket.Principal.Claims.Select(c => (c.Type, c.Value)).ToList());
        Assert.Equal([(SessionCookie.TenantId, alAmin.ToString("D")), (SessionCookie.UserId, omar.ToString("D"))],
            selectTicket.Principal.Claims.Select(c => (c.Type, c.Value)).OrderBy(c => c.Type).ToList());

        // The ticket's properties are the cookie handler's own bookkeeping (issue and expiry times).
        foreach (var ticket in new[] { loginTicket, selectTicket })
        {
            Assert.All(ticket.Properties.Items.Keys, key => Assert.StartsWith(".", key));
            var everything = string.Join("|", ticket.Properties.Items.Select(i => i.Key + "=" + i.Value)
                .Concat(ticket.Principal.Claims.Select(c => c.Type + "=" + c.Value))).ToLowerInvariant();
            foreach (var forbidden in new[] { "scope", "permission", "role", "membership", "manage" })
                Assert.DoesNotContain(forbidden, everything);
        }
    }

    // The tenant-selection path (4.3-b, Test 7): a user with no active tenant → app.user_id alone; the unit of work
    // resolves nothing on the second axis, since there is no tenant to resolve it in.
    [Fact]
    public async Task UnitOfWork_UserWithoutTenant_SetsUserIdAlone_NoSecondAxis()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);

        var (scope, memberships) = await UnitOfWork.RunAsync(db, new SessionContext(fixture.W1User, null),
            async (context, ct) => (context.Scope, await context.Memberships.Select(m => m.Id).ToListAsync(ct)));

        Assert.Null(scope);
        Assert.Equal([fixture.W1Membership], memberships);
        Assert.Equal([$"SET LOCAL app.user_id = '{fixture.W1User}'"], recorder.Commands.Where(c => c.Contains("SET LOCAL")).ToList());
    }

    private static WebApplicationFactory<Program> Api() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            foreach (var role in new[] { "app_user", "authenticator", "provisioner" })
                web.UseSetting($"ConnectionStrings:{role}", WhiteBoxFixture.ConnectionString(role));
        });

    private static (string Value, HashSet<string> Attributes) SessionCookieOf(HttpResponseMessage response)
    {
        var header = Assert.Single(response.Headers.GetValues("Set-Cookie"), h => h.StartsWith("session="));
        var parts = header.Split(';', StringSplitOptions.TrimEntries);
        return (parts[0]["session=".Length..], parts.Skip(1).Select(p => p.ToLowerInvariant()).ToHashSet());
    }

    private async Task<Guid> ScalarGuidAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No row: {sql}"));
    }
}

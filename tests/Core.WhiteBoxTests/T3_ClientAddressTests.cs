using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Core.WhiteBoxTests;

// [W] The client's address behind a reverse proxy (OPEN_ITEMS 85). The attempt limiter keys on the address
// (Amendment 01 §5.2) and auth_attempts records it, so whoever decides the address decides the limiter:
//   * X-Forwarded-For is believed from a proxy named in Network:TrustedProxies, and from nobody else;
//   * one hop only — the last entry, the one the proxy wrote;
//   * with no proxy configured, no header is read at all — and loopback is never trusted unless configured;
//   * a malformed entry stops Api from starting.
// Each request's connection address is set by a startup filter (a TestServer request has none), ahead of Api's own
// pipeline — where a real connection's address is. The address Api settled on is read back from auth_attempts.
[Collection(WhiteBoxCollection.Name)]
public class T3_ClientAddressTests(WhiteBoxFixture fixture)
{
    private const string Proxy = "10.213.47.1";
    private const string Client = "203.0.113.7";
    private const string OtherClient = "203.0.113.8";
    private const string Stranger = "198.51.100.9";
    private static readonly string[] Addresses = [Proxy, Client, OtherClient, Stranger];

    [Theory]
    [InlineData("10.213.47.0/29")]
    [InlineData("proxy.internal")]
    [InlineData("10.213.047.1")]
    [InlineData("not an address")]
    public Task TrustedProxies_Malformed_ApiRefusesToStart(string entry) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(null, ("Network:TrustedProxies:0", entry));

            var error = Assert.ThrowsAny<Exception>(() => api.Services);

            Assert.Contains(Chain(error), e => e is InvalidOperationException && e.Message.StartsWith("Network:TrustedProxies") &&
                e.Message.Contains(entry));
        });

    // The framework's own switch installs the middleware trusting every peer: refused, not merely discouraged.
    [Fact]
    public Task FrameworkForwardedHeadersSwitch_ApiRefusesToStart() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(null, ("FORWARDEDHEADERS_ENABLED", "true"));

            var error = Assert.ThrowsAny<Exception>(() => api.Services);

            Assert.Contains(Chain(error), e => e is InvalidOperationException && e.Message.Contains("FORWARDEDHEADERS_ENABLED"));
        });

    // Ordering: a trusted proxy's request is still a request — CSRF refuses it without Origin, as any other.
    [Fact]
    public Task FromTheProxy_CsrfStillApplies() =>
        WithApiAsync(Proxy, async client =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { username = NewUsername(), password = "wrong" }),
            };
            request.Headers.Add(ConnectionAddress.Header, Proxy);
            request.Headers.Add("X-Forwarded-For", Client);
            request.Headers.Remove("Origin");
            client.DefaultRequestHeaders.Remove("Origin");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("""{"error":"csrf_rejected"}""", await response.Content.ReadAsStringAsync());
        });

    // No proxy configured (development, CI): the header is ignored — a client cannot choose its own address.
    [Fact]
    public Task NoProxyConfigured_TheHeaderIsIgnored() =>
        WithApiAsync(trustedProxy: null, async client =>
            Assert.Equal(Stranger, await RecordedAddressAsync(client, connection: Stranger, forwardedFor: Client)));

    // From the proxy: the header is the client.
    [Fact]
    public Task FromTheProxy_TheHeaderIsTheClient() =>
        WithApiAsync(Proxy, async client =>
            Assert.Equal(Client, await RecordedAddressAsync(client, connection: Proxy, forwardedFor: Client)));

    // Kestrel listening on both stacks sees an IPv4 peer as an IPv4-mapped IPv6 address: still the proxy.
    [Fact]
    public Task FromTheProxy_AsAMappedIPv6Address_TheHeaderIsTheClient() =>
        WithApiAsync(Proxy, async client =>
            Assert.Equal(Client, await RecordedAddressAsync(client, connection: "::ffff:" + Proxy, forwardedFor: Client)));

    // From anyone else — a request that reached Api without the proxy — the header is ignored.
    [Fact]
    public Task NotFromTheProxy_TheHeaderIsIgnored() =>
        WithApiAsync(Proxy, async client =>
            Assert.Equal(Stranger, await RecordedAddressAsync(client, connection: Stranger, forwardedFor: Client)));

    // ASP.NET's own defaults trust loopback; here only what is configured is trusted, with or without a proxy.
    [Theory]
    [InlineData(null, "127.0.0.1")]
    [InlineData(null, "::1")]
    [InlineData(Proxy, "127.0.0.1")]
    [InlineData(Proxy, "::1")]
    public Task FromLoopback_TheHeaderIsIgnored(string? trustedProxy, string loopback) =>
        WithApiAsync(trustedProxy, async client =>
            Assert.Equal(loopback, await RecordedAddressAsync(client, connection: loopback, forwardedFor: Client)));

    // One hop: what the client wrote before the proxy's entry is never read.
    [Fact]
    public Task FromTheProxy_OnlyTheLastEntryIsRead() =>
        WithApiAsync(Proxy, async client =>
            Assert.Equal(Client, await RecordedAddressAsync(client, connection: Proxy, forwardedFor: $"{Stranger}, {Client}")));

    // The point of all of it: behind the proxy, the ip ceiling is each client's own — one client reaching it does not
    // refuse another, whose right password still signs in.
    [Fact]
    public Task BehindTheProxy_TheCeilingIsEachClientsOwn() =>
        WithApiAsync(Proxy, async client =>
        {
            var username = NewUsername();
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, "wrong", Proxy, Client)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, "wrong", Proxy, Client)).StatusCode);

            Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, username, "wrong", Proxy, Client)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await LoginAsync(client, "khaled", "khaled-seed-password", Proxy, OtherClient)).StatusCode);
        }, ("Identity:LoginLimits:PerIpCeiling", "2"), ("Identity:LoginLimits:FreeFailures", "10"));

    private Task WithApiAsync(string? trustedProxy, Func<HttpClient, Task> body, params (string Key, string Value)[] settings) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            try
            {
                var all = trustedProxy is null ? settings : [.. settings, ("Network:TrustedProxies:0", trustedProxy)];
                await using var api = InProcessApi.Create(settings: all)
                    .WithWebHostBuilder(web => web.ConfigureServices(services =>
                        services.AddSingleton<IStartupFilter>(new ConnectionAddress())));
                using var client = api.CreateClient(new() { BaseAddress = new Uri(InProcessApi.Origin) });
                await body(client);
            }
            finally
            {
                await ClearAttemptsAsync();
            }
        });

    /// <summary>One failed sign-in under a username of its own, and the address auth_attempts recorded for it.</summary>
    private async Task<string?> RecordedAddressAsync(HttpClient client, string connection, string forwardedFor)
    {
        var username = NewUsername();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, "wrong", connection, forwardedFor)).StatusCode);

        await using var db = new NpgsqlConnection(fixture.MigratorConnectionString);
        await db.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT ip_address FROM auth_attempts WHERE username_entered = @u", db);
        command.Parameters.AddWithValue("u", username);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string username, string password, string connection, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username, password }),
        };
        request.Headers.Add(ConnectionAddress.Header, connection);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return client.SendAsync(request);
    }

    private static string NewUsername() => "wb-proxy-" + Guid.CreateVersion7().ToString("N")[^12..];

    /// <summary>
    /// This test's own rows — its usernames, and the documentation addresses only it uses (the seed account's sign-in
    /// from <see cref="OtherClient"/>) — so every other attempt stays, loopback ones included.
    /// </summary>
    private async Task ClearAttemptsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM auth_attempts WHERE username_entered LIKE 'wb-proxy-%' OR ip_address = ANY(@a)", connection);
        command.Parameters.AddWithValue("a", Addresses);
        await command.ExecuteNonQueryAsync();
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }

    /// <summary>Gives each TestServer request the connection address its header names, before Api's pipeline.</summary>
    private sealed class ConnectionAddress : IStartupFilter
    {
        public const string Header = "X-Test-Connection-Address";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, following) =>
            {
                if (http.Request.Headers.TryGetValue(Header, out var value) && IPAddress.TryParse(value.ToString(), out var address))
                    http.Connection.RemoteIpAddress = address;
                await following();
            });
            next(app);
        };
    }
}

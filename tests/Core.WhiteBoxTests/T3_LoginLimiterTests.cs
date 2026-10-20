using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Core.WhiteBoxTests;

// [W] The attempt limiter (PLATFORM_CORE Amendment 01 §5.2, OPEN_ITEMS 16), with numbers of this test's own:
// the delays, the window and the ip ceiling are configuration, and the deployed numbers are settled at launch,
// so the mechanism is proven here rather than against whatever those numbers turn out to be.
//   * an escalating delay per (username + ip), growing with the window's failures;
//   * a ceiling per ip, refusing with 429 before the password is read — a correct password is refused too;
//   * and no account lockout: the same account, from another address, still logs in.
// The address of each request is set by a startup filter, because a TestServer request has none.
[Collection(WhiteBoxCollection.Name)]
public class T3_LoginLimiterTests(WhiteBoxFixture fixture)
{
    private const string Password = "khaled-seed-password";

    [Fact]
    public Task TheDelayEscalatesWithTheFailures_TheCeilingRefusesBeforeThePassword_AndNoAccountIsLockedOut() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            // The failures are counted for whatever was typed, existing or not (that is the point of §5.2: the
            // delay follows the typed username, never whether it exists), so they run against a username of this
            // test's own and leave the seed's accounts alone.
            var username = "wb-limiter-" + Guid.CreateVersion7().ToString("N")[^12..];
            try
            {
                await using var api = InProcessApi.Create(settings:
                [
                    ("Identity:LoginLimits:Window", "00:15:00"),
                    // The first failure is free, the second costs 0.6 s, and that is the cap.
                    ("Identity:LoginLimits:FreeFailures", "1"),
                    ("Identity:LoginLimits:DelaySecondsByFailure", "0.6"),
                    ("Identity:LoginLimits:PerIpCeiling", "3"),
                ]).WithWebHostBuilder(web => web.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter>(new ClientAddress())));
                using var client = api.CreateClient(new() { BaseAddress = new Uri(InProcessApi.Origin) });

                var first = await TimedFailureAsync(client, username, "10.0.0.1");
                var second = await TimedFailureAsync(client, username, "10.0.0.1");

                Assert.True(first < TimeSpan.FromMilliseconds(500), $"the first failure waited {first.TotalMilliseconds:F0} ms");
                Assert.True(second >= TimeSpan.FromMilliseconds(500), $"the second failure waited {second.TotalMilliseconds:F0} ms");

                // The third failure reaches the ceiling (three failures from this address in the window): from
                // then on the address is refused with 429 — and the refusal does not read the password, so the
                // right one is refused too.
                await FailAsync(client, username, "10.0.0.1");
                var refused = await LoginAsync(client, "khaled", Password, "10.0.0.1");
                Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
                Assert.Equal("""{"error":"too_many_attempts"}""", await refused.Content.ReadAsStringAsync());

                // No lockout (OPEN_ITEMS 16): the ceiling is the address's, not the account's — a real account
                // from another address signs in while this one is refused.
                var elsewhere = await LoginAsync(client, "khaled", Password, "10.0.0.2");
                Assert.Equal(HttpStatusCode.NoContent, elsewhere.StatusCode);
            }
            finally
            {
                await ClearAttemptsAsync();
            }
        });

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password, string ip)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { username, password }),
        };
        request.Headers.Add(ClientAddress.Header, ip);
        return client.SendAsync(request);
    }

    private static async Task FailAsync(HttpClient client, string username, string ip) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, username, "not-the-password", ip)).StatusCode);

    private static async Task<TimeSpan> TimedFailureAsync(HttpClient client, string username, string ip)
    {
        var clock = Stopwatch.StartNew();
        await FailAsync(client, username, ip);
        return clock.Elapsed;
    }

    /// <summary>This test's own rows, by the addresses only it uses: it leaves every other attempt in place.</summary>
    private async Task ClearAttemptsAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM auth_attempts WHERE ip_address IN ('10.0.0.1', '10.0.0.2')", connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Gives each TestServer request the address its header names, which Api reads as the caller's.</summary>
    private sealed class ClientAddress : IStartupFilter
    {
        public const string Header = "X-Test-Client-Address";

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (http, following) =>
            {
                if (http.Request.Headers.TryGetValue(Header, out var value)
                    && System.Net.IPAddress.TryParse(value.ToString(), out var address))
                    http.Connection.RemoteIpAddress = address;
                await following();
            });
            next(app);
        };
    }
}

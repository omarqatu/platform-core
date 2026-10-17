using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Core.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Core.WhiteBoxTests;

/// <summary>A sender that answers at once and keeps what it was given — the provider, replaced (there is none in CI).</summary>
internal sealed class CapturingSender : INotificationSender
{
    private readonly List<NotificationMessage> sent = [];

    public NotificationChannel Channel => NotificationChannel.Email;

    public IReadOnlyList<NotificationMessage> Sent
    {
        get { lock (sent) return sent.ToArray(); }
    }

    public Task<NotificationDelivery> SendAsync(NotificationMessage message, CancellationToken cancellationToken)
    {
        lock (sent) sent.Add(message);
        return Task.FromResult(NotificationDelivery.Delivered("captured"));
    }
}

// [W] POST /auth/password/forgot (PLATFORM_CORE Amendment 01 §5.1, §5.2; OPEN_ITEMS 51, 53).
// What is proven here and not in [B]: the message that leaves the path (its channel, its recipient, that there
// is exactly one, and that the token is not in the response), and the shape of the answer's timing — both need
// the sender replaced and the floor set, which is configuration, not an observable of the deployed system.
[Collection(WhiteBoxCollection.Name)]
public class T3_ForgotPasswordTests(WhiteBoxFixture fixture)
{
    private const string Forgot = "/api/auth/password/forgot";

    // A seeded address, and one that belongs to nobody. The seeded account's password is never changed here:
    // the path mints a token, and nothing in this file consumes one.
    private const string Known = "omar@seed.test";
    private const string Unknown = "nobody-at-all@seed.test";

    private const string Floor = "00:00:00.4000000";

    private Task<long> UnusedTokensAsync(string email) => TokensAsync(email, "AND t.used_at IS NULL");

    private Task<long> AllTokensAsync(string email) => TokensAsync(email, "");

    private async Task<long> TokensAsync(string email, string liveOnly)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        // The fragment goes on a line of its own, interpolated, never concatenated onto the line above: a raw
        // string literal does not keep the newline before its closing quotes, so `... = @e" + "AND t.used_at ...`
        // produced `= @eAND t.used_at`, and the parameter name swallowed the AND — 42601 from the server.
        await using var command = new NpgsqlCommand(
            $"""
             SELECT count(*) FROM password_reset_tokens t
               JOIN users u ON u.id = t.user_id
               JOIN persons p ON p.id = u.person_id
              WHERE lower(btrim(p.email)) = @e
                {liveOnly}
             """, connection);
        command.Parameters.AddWithValue("e", email);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static (WebApplicationFactory<Program> Api, CapturingSender Sender) Build()
    {
        var sender = new CapturingSender();
        var api = InProcessApi.Create(settings:
            [
                ("Identity:ForgotPassword:MinimumResponseTime", Floor),
                ("Identity:ForgotPassword:TokenLifetime", "00:30:00"),
                // The address is the same for every request here, so the limiter must not start charging for it
                // mid-test and turn a timing comparison into a comparison of delays.
                ("Identity:LoginLimits:FreeFailures", "200"),
                ("Identity:LoginLimits:PerIpCeiling", "0"),
            ])
            .WithWebHostBuilder(web => web.ConfigureTestServices(services =>
            {
                services.RemoveAll<INotificationSender>();
                services.AddSingleton<INotificationSender>(sender);
            }));
        return (api, sender);
    }

    // The whole contract of §5.1 in one path: the same 204 either way, a token for the address that has an
    // account and none for the address that does not, exactly one message out, and nothing in the response that
    // a caller could use to tell the two apart — body, headers or time.
    [Fact]
    public Task AKnownAndAnUnknownAddress_AnswerAlike_AndOnlyTheKnownOneMintsATokenAndAMessage() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            var (api, sender) = Build();
            await using var host = api;
            using var client = api.CreateClient();

            var before = await AllTokensAsync(Known);

            var known = await client.PostAsJsonAsync(Forgot, new { email = Known });
            var unknown = await client.PostAsJsonAsync(Forgot, new { email = Unknown });

            Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, unknown.StatusCode);
            Assert.Equal("", await known.Content.ReadAsStringAsync());
            Assert.Equal("", await unknown.Content.ReadAsStringAsync());
            // No session is issued, and nothing is set on the browser (§5.1).
            Assert.False(known.Headers.Contains("Set-Cookie"));

            // The known address gained a token, and exactly one of its tokens is live: a request supersedes
            // whatever was outstanding, so live tokens never accumulate however many requests came before —
            // which is why the count of live ones is asserted absolutely and the total relatively.
            Assert.Equal(before + 1, await AllTokensAsync(Known));
            Assert.Equal(1, await UnusedTokensAsync(Known));
            // The unknown address got nothing at all: not a live token, not a consumed one, no row.
            Assert.Equal(0, await AllTokensAsync(Unknown));

            // One message, to that address, on the email channel — and the second request produced none.
            await Eventually(() => sender.Sent, m => m.Count >= 1);
            // The dispatcher sends outside the request, so a second message would arrive late, not never.
            await Task.Delay(300);
            var message = Assert.Single(sender.Sent);
            Assert.Equal(NotificationChannel.Email, message.Channel);
            Assert.Equal(Known, message.Recipient);
            Assert.Contains("#token=", message.Body);
        });

    // The second request supersedes the first: the amendment's table is single-use and no application role holds
    // DELETE on it, so the earlier token is consumed, not removed, and exactly one live token remains.
    [Fact]
    public Task AsecondRequest_ConsumesTheFirstToken_AndLeavesExactlyOneLive() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            var (api, _) = Build();
            await using var host = api;
            using var client = api.CreateClient();

            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Forgot, new { email = Known })).StatusCode);
            var afterFirst = await UnusedTokensAsync(Known);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(Forgot, new { email = Known })).StatusCode);

            Assert.Equal(afterFirst, await UnusedTokensAsync(Known));
        });

    // OPEN_ITEMS 51: the answer's time must not say whether the address has an account. The floor dominates both
    // cases — which it can only do because the provider round trip is not in the path (INotificationDispatch) —
    // so the assertion is that each answer waits for the floor and that the two do not differ by anything like
    // the work that distinguishes them. Generous bounds: this runs on CI's machine, not a quiet one.
    [Fact]
    public Task TheAnswerTakesTheSameTime_ForAnAddressWithAnAccountAndOneWithout() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            var (api, _) = Build();
            await using var host = api;
            using var client = api.CreateClient();
            var floor = TimeSpan.Parse(Floor);

            // The first request of a host pays for the first connection and the first query plan; it is not the
            // measurement, it is what makes the measurement about the path.
            await client.PostAsJsonAsync(Forgot, new { email = Known });
            await client.PostAsJsonAsync(Forgot, new { email = Unknown });

            var known = await MedianAsync(client, Known);
            var unknown = await MedianAsync(client, Unknown);

            Assert.True(known >= floor, $"an address with an account answered in {known}, below the floor {floor}");
            Assert.True(unknown >= floor, $"an address without one answered in {unknown}, below the floor {floor}");
            var difference = (known - unknown).Duration();
            Assert.True(difference < floor / 2,
                $"the two answers differ by {difference}: an account's existence is visible in the response time");
        });

    private static async Task<TimeSpan> MedianAsync(HttpClient client, string email)
    {
        var runs = new List<TimeSpan>();
        for (var i = 0; i < 3; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var response = await client.PostAsJsonAsync(Forgot, new { email });
            runs.Add(Stopwatch.GetElapsedTime(started));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }
        runs.Sort();
        return runs[runs.Count / 2];
    }

    /// <summary>The dispatcher sends outside the request, so what it did is awaited here rather than assumed.</summary>
    private static async Task<IReadOnlyList<NotificationMessage>> Eventually(
        Func<IReadOnlyList<NotificationMessage>> read, Func<IReadOnlyList<NotificationMessage>, bool> until)
    {
        for (var i = 0; i < 100; i++)
        {
            var value = read();
            if (until(value))
                return value;
            await Task.Delay(50);
        }
        return read();
    }
}

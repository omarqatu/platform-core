using System.Net;
using System.Net.Http.Json;

namespace Conformance;

/// <summary>
/// [B] <c>POST /api/auth/password/forgot</c> (PLATFORM_CORE Amendment 01 §5.1; OPEN_ITEMS 51, 53), over SQL and
/// HTTP alone. What a client can see is one answer for every address; what the database shows is that a token
/// was minted only where there is an account to mint it for. Each test uses a tenant and an account of its own
/// (<see cref="World"/>), so the seed contract stays as PROOF_SPEC §7 states it.
/// </summary>
public class T3_ForgotPasswordTests
{
    private const string Forgot = "/auth/password/forgot";

    private static Task<long> LiveTokensAsync(Guid userId) => Seed.CountAsMigratorAsync(
        "SELECT count(*) FROM password_reset_tokens WHERE user_id = @u AND used_at IS NULL", ("u", userId));

    private static Task<long> TokensAsync(Guid userId) => Seed.CountAsMigratorAsync(
        "SELECT count(*) FROM password_reset_tokens WHERE user_id = @u", ("u", userId));

    // The address of an account: 204, and one live token for that account and no other.
    [Fact]
    public async Task AnAddressWithAnAccount_Answers204_AndMintsOneToken()
    {
        using var world = await World.BootstrapAsync("forgot-known");
        using var anonymous = new Browser();

        var response = await anonymous.Client.PostAsJsonAsync(Forgot, new { email = world.Owner.Email });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());
        // The token is delivered to the address, never to the caller: no session, nothing in the response.
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, await LiveTokensAsync(world.Owner.UserId));
    }

    // The same answer for an address nobody holds, and nothing written: the uniform 204 of §5.1, and the
    // absence of a token is what makes it uniform rather than merely polite.
    [Fact]
    public async Task AnAddressWithNoAccount_Answers204_AndMintsNothing()
    {
        using var world = await World.BootstrapAsync("forgot-unknown");
        using var anonymous = new Browser();
        var nobody = "nobody-" + World.Tag() + "@t4.test";

        var response = await anonymous.Client.PostAsJsonAsync(Forgot, new { email = nobody });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await TokensAsync(world.Owner.UserId));
        Assert.Equal(0, await Seed.CountAsMigratorAsync(
            """
            SELECT count(*) FROM password_reset_tokens t
              JOIN users u ON u.id = t.user_id
              JOIN persons p ON p.id = u.person_id
             WHERE lower(btrim(p.email)) = @e
            """, ("e", nobody)));
    }

    // The address is matched under the rule persons_email_normalized_key states — case and surrounding spaces
    // are not a different address (§4.5, OPEN_ITEMS 41) — so recovery is not lost to a capital letter.
    [Fact]
    public async Task TheAddressIsMatchedNormalized()
    {
        using var world = await World.BootstrapAsync("forgot-normalized");
        using var anonymous = new Browser();

        var response = await anonymous.Client.PostAsJsonAsync(Forgot,
            new { email = "  " + world.Owner.Email.ToUpperInvariant() + " " });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await LiveTokensAsync(world.Owner.UserId));
    }

    // A second request supersedes the first: two tokens exist, one of them live. The table is single-use and no
    // application role holds DELETE on it, so the superseded one is consumed, not removed.
    [Fact]
    public async Task AsecondRequest_LeavesTheFirstTokenConsumed()
    {
        using var world = await World.BootstrapAsync("forgot-again");
        using var anonymous = new Browser();

        await anonymous.Client.PostAsJsonAsync(Forgot, new { email = world.Owner.Email });
        var second = await anonymous.Client.PostAsJsonAsync(Forgot, new { email = world.Owner.Email });

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(2, await TokensAsync(world.Owner.UserId));
        Assert.Equal(1, await LiveTokensAsync(world.Owner.UserId));
    }

    // A body that is not a request at all is refused as one, with the invalid_request code — it names no
    // address, so it says nothing about any account.
    [Theory]
    [InlineData("{}")]
    [InlineData("""{"email": ""}""")]
    public async Task AMalformedRequestIsRefused(string body)
    {
        using var anonymous = new Browser();

        var response = await anonymous.Client.PostAsync(Forgot,
            new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", (await anonymous.JsonAsync(response)).GetProperty("error").GetString());
    }
}

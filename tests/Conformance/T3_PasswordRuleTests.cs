using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Conformance;

/// <summary>
/// [B] The password rule (OPEN_ITEMS 62, decided by the project owner on 2026-10-17): <b>minimum length 12, no
/// composition rules</b>, refused server-side with <c>password_too_short</c>.
/// <para>
/// The rule is proven at <b>all four</b> entry points that write a password, over HTTP alone:
/// <c>POST /auth/password/reset</c>, <c>PUT /me/password</c>, <c>POST /invitations/accept</c> (the new-account
/// branch) and <c>POST /provision/tenants</c>. The fourth is registered in Development and CI only (T4.17) —
/// which is exactly why it is the one that gets forgotten, and it is what creates the accounts every other test
/// signs in as.
/// </para>
/// <para>
/// Each case is proven at the boundary and not merely somewhere below it: eleven characters are refused and
/// twelve are accepted, by the same request otherwise unchanged. And each refusal is proven to have
/// <b>written nothing</b> — the old password still signs in, the token is still unused, no account was created —
/// because a rule that refuses the response but writes the row would be worse than no rule.
/// </para>
/// <para>
/// <b>Not <c>POST /auth/login</c></b>: existing passwords are not forced to rotate, so a password shorter than
/// the rule must still sign in. That half of the decision is proven in <c>T3_PasswordRuleTests</c> of the
/// white-box suite [W] rather than here — an account with such a password can no longer be made through the
/// API, which is the rule working, and this suite holds no project reference and so cannot write the hash of
/// one itself.
/// </para>
/// </summary>
public class T3_PasswordRuleTests
{
    private const string Reset = "/auth/password/reset";
    private const string Change = "/me/password";
    private const string Bootstrap = "/provision/tenants";
    private const string TooShort = "password_too_short";

    /// <summary>The ceiling every password endpoint held before the rule and still holds (PasswordPolicy.MaximumLength).</summary>
    private const int PasswordCeiling = 1024;

    private const string InvalidRequestBody = """{"error":"invalid_request"}""";

    /// <summary>Eleven characters: one short of the rule, and the longest password it refuses.</summary>
    private const string ElevenCharacters = "eleven-char";

    /// <summary>Twelve characters: the shortest password the rule admits. Nothing else about it is notable —
    /// no symbol, no digit, no capital letter — which is the half of the decision that says composition is not
    /// a rule here.</summary>
    private const string TwelveCharacters = "twelvechars!";

    private static async Task AssertTooShortAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TooShort, body.RootElement.GetProperty("error").GetString());
    }

    // ---- 1. POST /auth/password/reset ----

    // The rule is checked before the token is read, so a refused password leaves the link usable: the same token
    // then sets a password of twelve. A one-use link spent on a password the server was never going to write
    // would send the user back to forgot for a mistake the screen already explains.
    [Fact]
    public async Task Reset_RefusesElevenCharacters_WithoutConsumingTheToken_AndThenAcceptsTwelve()
    {
        using var world = await World.BootstrapAsync("pw-rule-reset");
        var token = await T3_PasswordTests.IssueTokenAsync(world.Owner.UserId);
        using var anonymous = new Browser();

        await AssertTooShortAsync(await anonymous.Client.PostAsJsonAsync(Reset,
            new { token, new_password = ElevenCharacters }));

        // Nothing was written: the token is still live, and the old password still signs in.
        Assert.Equal(0, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM password_reset_tokens WHERE user_id = @u AND used_at IS NOT NULL", ("u", world.Owner.UserId)));
        using (var withOld = new Browser())
            Assert.Equal(HttpStatusCode.NoContent, (await withOld.LoginAsync(world.Owner.Username, world.Owner.Password)).StatusCode);

        // The same token, one character longer: accepted.
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.Client.PostAsJsonAsync(Reset,
            new { token, new_password = TwelveCharacters })).StatusCode);
        using var withNew = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await withNew.LoginAsync(world.Owner.Username, TwelveCharacters)).StatusCode);
    }

    // ---- 2. PUT /me/password ----

    // The rule is checked before the current password is verified, so a refused new password costs no
    // auth_attempts row and no step of the limiter's escalating delay (Amendment 01 §5.2) — a user correcting
    // their own typo must not be rate-limited towards a lockout for it.
    [Fact]
    public async Task ChangePassword_RefusesElevenCharacters_WritesNothing_AndThenAcceptsTwelve()
    {
        using var world = await World.BootstrapAsync("pw-rule-change");
        var owner = world.Owner;
        var attemptsBefore = await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM auth_attempts WHERE username_entered = @u", ("u", owner.Username));

        var refused = await owner.Browser.PutAsync(Change,
            new { current_password = owner.Password, new_password = ElevenCharacters });

        Assert.Equal(HttpStatusCode.BadRequest, refused.Status);
        Assert.Equal($$"""{"error":"{{TooShort}}"}""", refused.Body);
        Assert.Equal(attemptsBefore, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM auth_attempts WHERE username_entered = @u", ("u", owner.Username)));
        // The session is untouched and the old password still signs in.
        Assert.Equal(HttpStatusCode.OK, (await owner.Browser.GetAsync("/me")).Status);
        using (var withOld = new Browser())
            Assert.Equal(HttpStatusCode.NoContent, (await withOld.LoginAsync(owner.Username, owner.Password)).StatusCode);

        var accepted = await owner.Browser.PutAsync(Change,
            new { current_password = owner.Password, new_password = TwelveCharacters });

        Assert.Equal(HttpStatusCode.NoContent, accepted.Status);
        using var withNew = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await withNew.LoginAsync(owner.Username, TwelveCharacters)).StatusCode);
    }

    // ---- 3. POST /invitations/accept, the new-account branch ----

    // This is where accounts are actually created, so it is the entry point most people ever meet the rule at.
    // A refusal creates no user and no membership: the invitation is still pending and can be accepted again.
    [Fact]
    public async Task AcceptNew_RefusesElevenCharacters_CreatesNothing_AndThenAcceptsTwelve()
    {
        using var world = await World.BootstrapAsync("pw-rule-accept");
        var (username, email, _) = World.Account("pw-rule-invitee");
        var (invited, invitation, token) = await world.InviteAsync(world.Owner, email, "viewer", "assigned");
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        using var anonymous = new Browser();

        await AssertTooShortAsync(await World.AcceptNewAsync(anonymous, world.TenantId, token, email, username, ElevenCharacters));

        // No account, no membership, and the invitation is untouched — still pending, not consumed.
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM invitations WHERE id = @i AND status = 'pending'", ("i", invitation)));

        // The same invitation, one character longer: accepted, and the account exists.
        Assert.Equal(HttpStatusCode.NoContent,
            (await World.AcceptNewAsync(anonymous, world.TenantId, token, email, username, TwelveCharacters)).StatusCode);
        using var joined = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await joined.LoginAsync(username, TwelveCharacters)).StatusCode);
    }

    // The rule's code says length, so it must not be the answer to a field that is malformed rather than short.
    // A password over the ceiling is invalid_request, as it was before the rule existed — "too short" would be
    // an untrue thing to tell the caller, and the code is a contract.
    [Fact]
    public async Task APasswordOverTheCeiling_IsStillInvalidRequest_NotTooShort()
    {
        using var world = await World.BootstrapAsync("pw-rule-ceiling");
        var token = await T3_PasswordTests.IssueTokenAsync(world.Owner.UserId);
        using var anonymous = new Browser();

        var response = await anonymous.Client.PostAsJsonAsync(Reset,
            new { token, new_password = new string('x', PasswordCeiling + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidRequestBody, await response.Content.ReadAsStringAsync());
        // And the token is still live: the request was refused before it was read.
        Assert.Equal(0, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM password_reset_tokens WHERE user_id = @u AND used_at IS NOT NULL", ("u", world.Owner.UserId)));
    }

    // ---- 4. POST /provision/tenants ----

    // The route that exists in Development and CI only (T4.17), and the route that creates the accounts the rest
    // of this suite signs in as. A refusal leaves no tenant behind.
    [Fact]
    public async Task Bootstrap_RefusesElevenCharacters_CreatesNoTenant_AndThenAcceptsTwelve()
    {
        var (username, email, _) = World.Account("pw-rule-boot");
        var name = $"T3 pw-rule {World.Tag()}";
        using var anonymous = new Browser();

        await AssertTooShortAsync(await anonymous.Client.PostAsJsonAsync(Bootstrap,
            new { tenant_name = name, full_name = username, email, username, password = ElevenCharacters }));

        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", name)));
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));

        var created = await anonymous.Client.PostAsJsonAsync(Bootstrap,
            new { tenant_name = name, full_name = username, email, username, password = TwelveCharacters });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var signedIn = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await signedIn.LoginAsync(username, TwelveCharacters)).StatusCode);
    }
}

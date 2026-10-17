using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Conformance;

/// <summary>
/// [B] Password recovery and change (PLATFORM_CORE Amendment 01 §2.1, §2.2, §5.1; OPEN_ITEMS 47), over SQL and
/// HTTP alone. The tokens are written as <c>authenticator</c> — the role the amendment gives that write — with the
/// hash of a value this test knows, because the forgot path that would mint one is not built in this task (it
/// waits for the e-mail sender, OPEN_ITEMS 53): the contract proven here is the reset endpoint's, not the way the
/// token reached its owner. Every test works on a tenant and an account of its own, so the seed contract stays as
/// PROOF_SPEC §7 states it.
/// </summary>
public class T3_PasswordTests
{
    private const string Reset = "/auth/password/reset";
    private const string Change = "/me/password";

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A recovery token for a user, written as authenticator — its only writes are this table and auth_attempts.</summary>
    private static async Task<string> IssueTokenAsync(Guid userId, TimeSpan? lifetime = null, DateTime? usedAt = null)
    {
        var token = NewToken();
        await using var connection = await Target.OpenAsync(Target.Authenticator);
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO password_reset_tokens (id, user_id, token_hash, requested_at, expires_at, used_at, request_ip)
            VALUES (@i, @u, @h, now(), now() + @l, @used, NULL)
            """, connection);
        command.Parameters.AddWithValue("i", Guid.CreateVersion7());
        command.Parameters.AddWithValue("u", userId);
        command.Parameters.AddWithValue("h", HashOf(token));
        command.Parameters.AddWithValue("l", lifetime ?? TimeSpan.FromMinutes(30));
        command.Parameters.AddWithValue("used", (object?)usedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
        return token;
    }

    private static Task<long> UsedCountAsync(string token) => Seed.CountAsMigratorAsync(
        "SELECT count(*) FROM password_reset_tokens WHERE token_hash = @h AND used_at IS NOT NULL", ("h", HashOf(token)));

    // The whole path: a valid token sets a new password, the old one stops working, the new one works — and the
    // token is consumed, so the same token cannot set a password twice (§2.2, §5.1).
    [Fact]
    public async Task Reset_SetsTheNewPassword_ConsumesTheToken_AndTheOldPasswordStops()
    {
        using var world = await World.BootstrapAsync("pw-reset");
        var token = await IssueTokenAsync(world.Owner.UserId);
        var newPassword = world.Owner.Password + "-reset";
        using var anonymous = new Browser();

        var reset = await anonymous.Client.PostAsJsonAsync(Reset, new { token, new_password = newPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        // No session is issued by the reset path (§5.1).
        Assert.False(reset.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, await UsedCountAsync(token));

        using var withOld = new Browser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await withOld.LoginAsync(world.Owner.Username, world.Owner.Password)).StatusCode);
        using var withNew = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await withNew.LoginAsync(world.Owner.Username, newPassword)).StatusCode);

        // A second use of the same token is refused, and changes nothing.
        var again = await anonymous.Client.PostAsJsonAsync(Reset, new { token, new_password = newPassword + "-twice" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        using var afterSecond = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await afterSecond.LoginAsync(world.Owner.Username, newPassword)).StatusCode);
    }

    // An unknown, an expired and an already-used token are refused alike — one code, nothing said about which
    // (§5.1) — and no password changes.
    [Theory]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("used")]
    public async Task Reset_RefusesAnUnknownExpiredOrUsedToken(string kind)
    {
        using var world = await World.BootstrapAsync("pw-refuse-" + kind);
        var token = kind switch
        {
            "unknown" => NewToken(),
            "expired" => await IssueTokenAsync(world.Owner.UserId, lifetime: TimeSpan.FromMinutes(-1)),
            _ => await IssueTokenAsync(world.Owner.UserId, usedAt: DateTime.UtcNow),
        };
        using var anonymous = new Browser();

        var response = await anonymous.Client.PostAsJsonAsync(Reset, new { token, new_password = "another-password" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"error":"invalid_token"}""", await response.Content.ReadAsStringAsync());
        using var browser = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.LoginAsync(world.Owner.Username, world.Owner.Password)).StatusCode);
    }

    // The stamp (§2.1): a password change ends every other session of that user on its next request, and the
    // session that changed it keeps working — its cookie is re-issued with the new stamp (§5.1).
    [Fact]
    public async Task ChangePassword_EndsEveryOtherSession_AndKeepsItsOwn()
    {
        using var world = await World.BootstrapAsync("pw-change");
        var owner = world.Owner;
        using var elsewhere = await SignedInAsync(owner.Username, owner.Password);
        Assert.Equal(HttpStatusCode.OK, (await elsewhere.GetAsync("/me")).Status);

        var newPassword = owner.Password + "-changed";
        var changed = await owner.Browser.PutAsync(Change, new { current_password = owner.Password, new_password = newPassword });

        Assert.Equal(HttpStatusCode.NoContent, changed.Status);
        // The other session's cookie carries the old stamp: 401 session_expired on its very next request.
        var (status, body) = await elsewhere.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("session_expired", body.GetProperty("error").GetString());
        // The session that changed it continues.
        Assert.Equal(HttpStatusCode.OK, (await owner.Browser.GetAsync("/me")).Status);
        // And the new password is the one that logs in.
        using var withNew = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await withNew.LoginAsync(owner.Username, newPassword)).StatusCode);
    }

    // A reset ends every session too, including the browser that asked for it: no cookie is issued (§5.1).
    [Fact]
    public async Task Reset_EndsTheSessionsOpenAtTheTime()
    {
        using var world = await World.BootstrapAsync("pw-reset-sessions");
        var token = await IssueTokenAsync(world.Owner.UserId);
        using var anonymous = new Browser();

        var reset = await anonymous.Client.PostAsJsonAsync(Reset, new { token, new_password = world.Owner.Password + "-r" });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        var (status, body) = await world.Owner.Browser.GetAsync("/me");
        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("session_expired", body.GetProperty("error").GetString());
    }

    // The wrong current password: 401, and nothing changes — the verification is the authenticator's, as login's is.
    [Fact]
    public async Task ChangePassword_WithTheWrongCurrentPassword_IsRefused()
    {
        using var world = await World.BootstrapAsync("pw-wrong-current");
        var owner = world.Owner;
        var attemptsBefore = await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM auth_attempts WHERE username_entered = @u AND NOT succeeded", ("u", owner.Username));

        var refused = await owner.Browser.PutAsync(Change, new { current_password = "not-the-password", new_password = "a-new-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal("""{"error":"invalid_credentials"}""", refused.Body);
        // The failure is written to auth_attempts, as login's is — the limiter counts it (§5.2).
        Assert.Equal(attemptsBefore + 1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM auth_attempts WHERE username_entered = @u AND NOT succeeded", ("u", owner.Username)));
        using var browser = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.LoginAsync(owner.Username, owner.Password)).StatusCode);
    }

    // Changing a password needs a session: no cookie, no path (§5.1 — the session column).
    [Fact]
    public async Task ChangePassword_WithoutASession_Is401()
    {
        using var anonymous = new Browser();
        var response = await anonymous.PutAsync(Change, new { current_password = "x", new_password = "y" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.Status);
    }

    // The table's surface (§3): authenticator alone, and its only write after the insert is consumption —
    // app_user cannot read a token at all, and no role can un-consume one (§4.1).
    [Fact]
    public async Task PasswordResetTokens_AreTheAuthenticatorsAlone_AndMoveOneWayOnly()
    {
        using var world = await World.BootstrapAsync("pw-surface");
        var token = await IssueTokenAsync(world.Owner.UserId);

        await using (var appUser = await Session.OpenAsync(Target.AppUser, user: world.Owner.UserId, tenant: world.TenantId))
            Assert.Equal("42501", await appUser.SqlStateOfAsync("SELECT count(*) FROM password_reset_tokens"));

        await using var authenticator = await Session.OpenAsync(Target.Authenticator);
        Assert.Equal(1, await authenticator.ExecuteAsync(
            "UPDATE password_reset_tokens SET used_at = now() WHERE token_hash = @h", ("h", HashOf(token))));
        // Consumed: the USING clause of password_reset_tokens_authenticator_use hides it from a second consumption,
        // and its WITH CHECK refuses putting used_at back to NULL.
        Assert.Equal(0, await authenticator.ExecuteAsync(
            "UPDATE password_reset_tokens SET used_at = now() WHERE token_hash = @h", ("h", HashOf(token))));
        Assert.Equal(0, await authenticator.ExecuteAsync(
            "UPDATE password_reset_tokens SET used_at = NULL WHERE token_hash = @h", ("h", HashOf(token))));
        // No application role may delete one: retention is migrator's procedure (§2.2).
        Assert.Equal("42501", await authenticator.SqlStateOfAsync("DELETE FROM password_reset_tokens"));
    }

    private static async Task<Browser> SignedInAsync(string username, string password)
    {
        var browser = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.LoginAsync(username, password)).StatusCode);
        return browser;
    }
}

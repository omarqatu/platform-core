using System.Net;
using System.Net.Http.Json;
using Core.Identity;
using Npgsql;

namespace Core.WhiteBoxTests;

// [W] The half of the password rule (OPEN_ITEMS 62) that says where the rule does *not* apply.
//
// The rule is minimum length 12 at every entry point that writes a password, and the Conformance suite [B]
// proves all four of them. Existing passwords, though, are not forced to rotate — whether they ever are is a
// separate question the project owner has not answered — so POST /auth/login must keep accepting a password
// shorter than the rule. Checking the rule there would lock out every account that existed before the rule did,
// and would tell an anonymous caller what the rule is.
//
// That cannot be proven over HTTP alone, which is why it is here rather than in Conformance: an account with
// such a password can no longer be created through the API, which is the rule working, so the account is made
// as migrator with a hash from Core's own hasher — the one PasswordHashing.Verify reads on the login path.
//
// The account is this test's own, created and removed here: the fixture's users are shared with the rest of the
// collection, and signing one of them in would move its last_login_at under another test's feet.
[Collection(WhiteBoxCollection.Name)]
public class T3_PasswordRuleTests(WhiteBoxFixture fixture) : IAsyncLifetime
{
    // Eleven characters: one short of the rule, and a password the API would now refuse to set.
    private const string ShorterThanTheRule = "eleven-char";

    private readonly Guid _person = Guid.CreateVersion7();
    private readonly Guid _user = Guid.CreateVersion7();
    private string _username = "";

    public async Task InitializeAsync()
    {
        _username = "wb-pw-rule-" + Guid.CreateVersion7().ToString("N")[^12..];
        await ExecuteAsMigratorAsync(
            """
            INSERT INTO persons (id, full_name, email, phone_e164, created_at)
              VALUES (@p, 'White-box password rule', @e, NULL, now());
            INSERT INTO users (id, person_id, user_type, username, status, language, theme, last_login_at, security_stamp)
              VALUES (@u, @p, 'employee', @n, 'active', NULL, NULL, NULL, @s);
            INSERT INTO user_password_credentials (user_id, password_hash, updated_at) VALUES (@u, @h, now());
            """,
            ("p", _person), ("u", _user), ("n", _username), ("e", _username + "@white-box.test"),
            ("s", Guid.CreateVersion7()), ("h", PasswordHashing.Hash(ShorterThanTheRule)));
    }

    public async Task DisposeAsync() => await ExecuteAsMigratorAsync(
        """
        DELETE FROM auth_attempts WHERE username_entered = @n;
        DELETE FROM user_password_credentials WHERE user_id = @u;
        DELETE FROM users WHERE id = @u;
        DELETE FROM persons WHERE id = @p;
        """, ("p", _person), ("u", _user), ("n", _username));

    [Fact]
    public Task Login_StillAcceptsAPasswordShorterThanTheRule() =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create();
            using var client = api.CreateClient(new() { BaseAddress = new Uri(InProcessApi.Origin) });

            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { username = _username, password = ShorterThanTheRule });

            // 204 and a session, not 400 password_too_short: the rule guards what is written, never what is read.
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.True(response.Headers.Contains("Set-Cookie"));
        });

    // And the policy itself, at its boundary, with no database in it at all: eleven refused, twelve accepted,
    // and nothing about composition — no symbol, no digit, no capital letter in the accepted value.
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("eleven-char", false)]       // 11
    [InlineData("twelvechars!", true)]       // 12
    [InlineData("twelvecharsx", true)]       // 12, and nothing but lowercase letters
    public void ThePolicyIsLengthAlone(string? password, bool acceptable) =>
        Assert.Equal(acceptable, PasswordPolicy.IsAcceptable(password));

    // The ceiling is unchanged by the rule: 1024 stands, 1025 does not.
    [Fact]
    public void TheMaximumIsUnchanged()
    {
        Assert.True(PasswordPolicy.IsAcceptable(new string('x', PasswordPolicy.MaximumLength)));
        Assert.False(PasswordPolicy.IsAcceptable(new string('x', PasswordPolicy.MaximumLength + 1)));
    }

    private async Task ExecuteAsMigratorAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

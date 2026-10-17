using Core.Data;
using Core.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Core.WhiteBoxTests;

// [W] The two units of work of password recovery (PLATFORM_CORE Amendment 01 §2.1, §2.2, §5.1), through Core
// directly with the command recorder on the app_user context:
//   * UoW-2 of the reset path runs with app.user_id alone — the user the consumed token named (the §4.3-b
//     exception, §0) — and sets no tenant and no second-axis variable;
//   * it writes the new hash and a new stamp with no audit entry and no AuditContextMissingException, although
//     the transaction names no tenant: the exemptions of §2.1 and §2.2 (OPEN_ITEMS 52 stays open for it);
//   * the token is consumed, and consuming it twice is loud.
[Collection(WhiteBoxCollection.Name)]
public class T3_PasswordPathTests(WhiteBoxFixture fixture) : IAsyncLifetime
{
    private const string FirstPassword = "white-box-first-password";

    public async Task InitializeAsync() => await ExecuteAsMigratorAsync(
        """
        INSERT INTO user_password_credentials (user_id, password_hash, updated_at) VALUES (@u, @h, now())
        ON CONFLICT (user_id) DO UPDATE SET password_hash = excluded.password_hash, updated_at = now();
        """, ("u", fixture.W1User), ("h", PasswordHashing.Hash(FirstPassword)));

    public async Task DisposeAsync() => await ExecuteAsMigratorAsync(
        """
        DELETE FROM password_reset_tokens WHERE user_id = @u;
        DELETE FROM user_password_credentials WHERE user_id = @u;
        """, ("u", fixture.W1User));

    [Fact]
    public async Task ResetPath_UoW2_RunsWithTheTokensUserAlone_AndWritesNoAuditEntry()
    {
        var token = await IssueTokenAsync(TimeSpan.FromMinutes(30));
        var stampBefore = await StampAsync();
        var auditBefore = await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE entity_id = @u", ("u", fixture.W1User));

        await using var authenticatorSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("authenticator"));
        await using var authenticator = AuthenticatorContext(authenticatorSource);
        var owner = await PasswordPaths.ConsumeResetTokenAsync(authenticator, token);

        // The one value that crosses from UoW-1 to UoW-2: the owner of the consumed row, never the request.
        Assert.Equal(fixture.W1User, owner);
        Assert.Equal(1, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM password_reset_tokens WHERE user_id = @u AND used_at IS NOT NULL", ("u", fixture.W1User)));

        await using var appUserSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(appUserSource, recorder);
        var stamp = await PasswordPaths.ApplyNewPasswordAsync(db, owner, "white-box-second-password");

        Assert.Equal([$"SET LOCAL app.user_id = '{fixture.W1User}'"],
            recorder.Commands.Where(c => c.Contains("SET LOCAL")).ToList());
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("app.tenant_id") || c.Contains("app.membership_id")
                                                   || c.Contains("app.scope_all") || c.Contains("app.can_manage"));
        // The stamp on the row is the one returned, and it is not the one the session held.
        Assert.NotEqual(stampBefore, stamp);
        Assert.Equal(stamp, await StampAsync());
        // No audit entry — and no AuditContextMissingException, although this transaction names no tenant (§2.1).
        Assert.Equal(auditBefore, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM audit_log WHERE entity_id = @u", ("u", fixture.W1User)));
        Assert.Equal(0, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM audit_log WHERE entity_type IN ('user_password_credentials', 'password_reset_tokens')"));
    }

    [Fact]
    public async Task ConsumingATokenTwice_IsLoud_AndAnExpiredOneIsRefused()
    {
        await using var source = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("authenticator"));
        await using var db = AuthenticatorContext(source);

        var token = await IssueTokenAsync(TimeSpan.FromMinutes(30));
        Assert.Equal(fixture.W1User, await PasswordPaths.ConsumeResetTokenAsync(db, token));
        await Assert.ThrowsAsync<PasswordResetRefusedException>(() => PasswordPaths.ConsumeResetTokenAsync(db, token));

        var expired = await IssueTokenAsync(TimeSpan.FromMinutes(-1));
        await Assert.ThrowsAsync<PasswordResetRefusedException>(() => PasswordPaths.ConsumeResetTokenAsync(db, expired));
        await Assert.ThrowsAsync<PasswordResetRefusedException>(() => PasswordPaths.ConsumeResetTokenAsync(db, "not-a-token"));
    }

    private static AuthenticatorDbContext AuthenticatorContext(NpgsqlDataSource source)
    {
        var options = new DbContextOptionsBuilder<AuthenticatorDbContext>();
        options.UseCoreDataAccess(source);
        return new AuthenticatorDbContext(options.Options);
    }

    private async Task<string> IssueTokenAsync(TimeSpan lifetime)
    {
        var token = RecoveryToken.New();
        await ExecuteAsMigratorAsync(
            """
            INSERT INTO password_reset_tokens (id, user_id, token_hash, requested_at, expires_at, used_at, request_ip)
            VALUES (@i, @u, @h, now(), now() + @l, NULL, NULL)
            """,
            ("i", Guid.CreateVersion7()), ("u", fixture.W1User), ("h", RecoveryToken.Hash(token)), ("l", lifetime));
        return token;
    }

    private async Task<Guid> StampAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT security_stamp FROM users WHERE id = @u", connection);
        command.Parameters.AddWithValue("u", fixture.W1User);
        return (Guid)(await command.ExecuteScalarAsync())!;
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

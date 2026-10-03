using Api.Endpoints;
using Core.Data;
using Core.Identity;
using Core.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;

namespace Core.WhiteBoxTests;

// Invitation acceptance, option C (decided by the project owner; docs/proposals/v1.17-acceptance) [W]:
// - the address match is the application's (§4.5), under one rule — the expression of persons_email_normalized_key —
//   and a mismatch is refused there, before the acceptance writes anything;
// - consuming the token is the rows-affected guard's critical write (§3.5/5): an acceptance whose token another
//   committed meanwhile writes nothing.
[Collection(WhiteBoxCollection.Name)]
public class AcceptanceMatchTests(WhiteBoxFixture fixture)
{
    // The application's rule and the index's are one: for each address, EmailAddress.Normalize equals
    // PostgreSQL's lower(btrim(…)) — spaces trimmed (and only spaces: btrim's default), case unified.
    [Theory]
    [InlineData(" Omar@Example.TEST ")]
    [InlineData("OMAR@example.test")]
    [InlineData("\tomar@example.test")]
    [InlineData("omar@example.test\n")]
    [InlineData("omar @example.test")]
    [InlineData("  Élodie@Exemple.TEST")]
    public async Task TheMatchRule_IsTheIndexExpression(string address)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT lower(btrim(@a))", connection);
        command.Parameters.AddWithValue("a", address);

        Assert.Equal((string)(await command.ExecuteScalarAsync())!, EmailAddress.Normalize(address));
    }

    // The new-account path asks whether the address has an account in the index's own expression — the query the
    // index serves — not by exact equality.
    [Fact]
    public async Task AccountExists_IsAskedInTheIndexExpression()
    {
        var recorder = new CommandRecorder();
        await using var provisionerSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        await using var appUserSource = fixture.CreateAppUserDataSource();
        var (tenant, token, email) = await InvitedAsync(provisionerSource, appUserSource, "wb-match-exists");
        await using var accepting = ProvisionerContext(provisionerSource, recorder);

        await Acceptance.RunAsync(accepting, new AcceptRequest(tenant, token,
            new NewAccount(email, "Invitee", "wb-match-" + Tag(), "wb-password")), null);

        Assert.Contains(recorder.Commands, c => System.Text.RegularExpressions.Regex.IsMatch(c, @"FROM persons WHERE lower\(btrim\(email\)\) = @",
            System.Text.RegularExpressions.RegexOptions.Singleline));
    }

    // An existing account whose address is not the invitation's — a different address, written in another case and
    // with surrounding spaces — is refused by the application: email_mismatch, and not one write sent.
    [Fact]
    public async Task Mismatch_RefusedByTheApplication_BeforeAnyWrite()
    {
        var recorder = new CommandRecorder();
        await using var provisionerSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        await using var appUserSource = fixture.CreateAppUserDataSource();
        // The stranger's own address is X; the invitation went to another address beside it, in another case and
        // with surrounding spaces: "  X-OTHER@… ".
        var (tenant, token, email) = await InvitedAsync(provisionerSource, appUserSource, "wb-match-mismatch",
            e => "  " + e.Replace("@", "-other@", StringComparison.Ordinal).ToUpperInvariant() + " ");
        var stranger = await ExistingAccountAsync(provisionerSource, email);
        await using var accepting = ProvisionerContext(provisionerSource, recorder);

        var refused = await Assert.ThrowsAsync<InvitationRefusedException>(() =>
            Acceptance.RunAsync(accepting, new AcceptRequest(tenant, token, null), stranger));

        Assert.Equal(ApiErrorCodes.EmailMismatch, refused.Code);
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("INSERT INTO", StringComparison.Ordinal)
                                                      || c.Contains("UPDATE ", StringComparison.Ordinal)
                                                      || c.Contains("DELETE FROM", StringComparison.Ordinal));
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", tenant), ("u", stranger)));
    }

    // Two acceptances of one token, by two users of the same person — both match the address, so only the token
    // stands between them. The second is held at the moment it consumes the token; the first runs whole and commits
    // meanwhile. The second's UPDATE … WHERE status = 'pending' then affects no row: the guard's error, and its
    // membership — written before — rolls back with it. One membership, deterministically.
    [Fact]
    public async Task TheTokenGuard_TheSecondAcceptanceOfOneToken_WritesNothing()
    {
        await using var provisionerSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        await using var appUserSource = fixture.CreateAppUserDataSource();
        var (tenant, token, email) = await InvitedAsync(provisionerSource, appUserSource, "wb-match-race");

        // One person, two users: the person's own account, and a second user of the same person (setup as migrator).
        var first = await ExistingAccountAsync(provisionerSource, email);
        var second = Guid.CreateVersion7();
        await ExecuteAsMigratorAsync(
            "INSERT INTO users (id, person_id, user_type, username, status) SELECT @id, person_id, 'customer', @n, 'active' FROM users WHERE id = @u",
            ("id", second), ("n", "wb-match-race-2-" + Tag()), ("u", first));

        var competitor = new CompetingAcceptance(async () =>
        {
            await using var db = ProvisionerContext(provisionerSource);
            await Acceptance.RunAsync(db, new AcceptRequest(tenant, token, null), first);
        });
        await using var held = ProvisionerContext(provisionerSource, competitor);

        var error = await Assert.ThrowsAsync<CriticalWriteException>(() =>
            Acceptance.RunAsync(held, new AcceptRequest(tenant, token, null), second));

        Assert.True(competitor.Ran);
        Assert.Equal(0, error.Affected);
        Assert.Equal(1, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM memberships m JOIN users u ON u.id = m.user_id WHERE m.tenant_id = @t AND u.id IN (@a, @b)",
            ("t", tenant), ("a", first), ("b", second)));
        Assert.Equal(1, await fixture.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @a",
            ("t", tenant), ("a", first)));
    }

    /// <summary>Runs <paramref name="competitor"/> to its commit just before this context consumes the token.</summary>
    private sealed class CompetingAcceptance(Func<Task> competitor) : DbCommandInterceptor
    {
        public bool Ran { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Ran && command.CommandText.Contains("UPDATE invitations", StringComparison.Ordinal))
            {
                Ran = true;
                await competitor();
            }
            return result;
        }
    }

    // ---- setup: a tenant of the test's own, an invitation from its owner (the real paths), and accounts.

    private static string Tag() => Guid.CreateVersion7().ToString("N")[^12..];

    private static BootstrapRequest Request(string label)
    {
        var tag = Tag();
        return new BootstrapRequest($"wb {label} {tag}", "Owner", $"wb-{label}-{tag}@white-box.test", $"wb-{label}-{tag}", "wb-password");
    }

    /// <summary>A tenant and an invitation in it; the address invited is <paramref name="invitedAs"/> of a fresh one.</summary>
    private async Task<(Guid Tenant, string Token, string Email)> InvitedAsync(NpgsqlDataSource provisionerSource, NpgsqlDataSource appUserSource,
        string label, Func<string, string>? invitedAs = null)
    {
        await using var provisioner = ProvisionerContext(provisionerSource);
        var boot = await Bootstrap.RunAsync(provisioner, Request(label));
        var owner = new SessionContext(boot.UserId, boot.TenantId);
        var viewer = await ScalarGuidAsync("SELECT id FROM roles WHERE tenant_id = @t AND code = 'viewer'", ("t", boot.TenantId));
        var email = $"{label}-{Tag()}@white-box.test";
        await using var app = WhiteBoxFixture.Context(appUserSource);
        var invited = await UnitOfWork.RunAsync(app, owner,
            (c, ct) => MemberAdministration.InviteAsync(c, owner, (invitedAs ?? (e => e))(email), viewer, "assigned", ct));
        return (boot.TenantId, invited.Token, email);
    }

    /// <summary>An account with this address: the owner of a tenant of its own.</summary>
    private static async Task<Guid> ExistingAccountAsync(NpgsqlDataSource provisionerSource, string email)
    {
        await using var provisioner = ProvisionerContext(provisionerSource);
        var tag = Tag();
        return (await Bootstrap.RunAsync(provisioner, new BootstrapRequest($"wb home {tag}", "Owner", email, $"wb-home-{tag}", "wb-password"))).UserId;
    }

    private static ProvisionerDbContext ProvisionerContext(NpgsqlDataSource dataSource, params IInterceptor[] extra)
    {
        var options = new DbContextOptionsBuilder<ProvisionerDbContext>();
        options.UseCoreDataAccess(dataSource);
        if (extra.Length > 0)
            options.AddInterceptors(extra);
        return new ProvisionerDbContext(options.Options);
    }

    private async Task<Guid> ScalarGuidAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No row: {sql}"));
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

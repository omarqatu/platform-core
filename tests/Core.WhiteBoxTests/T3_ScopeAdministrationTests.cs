using Api.Endpoints;
using Core.Data;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.2 T3, decision 36 as amended by the project owner: the two administrative surfaces of 4.8 are
// enforced in two layers, each proven on its own. Khaled (Al-Amin, operator, no core.scope.manage) against
// Layla's membership, on the seed contract; every transaction rolls back, so the seed is untouched.
//   1. The application (5): the explicit permission from the resolved scope → refused before any command
//      reaches the database.
//   2. The database (4.8): with the application's check bypassed — the write steps called directly — the
//      policies refuse on their own: an update affects zero rows (→ the rows-affected guard), an insert fails.
[Collection(WhiteBoxCollection.Name)]
public class T3_ScopeAdministrationTests(WhiteBoxFixture fixture)
{
    private static readonly Guid ClientC = new("0199c000-0000-7000-8000-0000000000a3");

    [Fact]
    public async Task Layer1_Application_RefusesModeChange_BeforeAnyCommand()
    {
        var (khaled, alAmin, layla) = await SeedAsync();
        var recorder = new CommandRecorder();
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);
        var session = new SessionContext(khaled, alAmin);

        var error = await Assert.ThrowsAsync<NotPermittedException>(() => UnitOfWork.RunAsync(db, session, (c, ct) =>
        {
            recorder.Clear();
            return ScopeAdministration.ChangeModeAsync(c, session, layla, "assigned", ct);
        }));

        Assert.Equal("core.scope.manage", error.Permission);
        Assert.Empty(recorder.Commands);
    }

    [Fact]
    public async Task Layer1_Application_RefusesAssignment_BeforeAnyCommand()
    {
        var (khaled, alAmin, layla) = await SeedAsync();
        var recorder = new CommandRecorder();
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);
        var session = new SessionContext(khaled, alAmin);

        await Assert.ThrowsAsync<NotPermittedException>(() => UnitOfWork.RunAsync(db, session, (c, ct) =>
        {
            recorder.Clear();
            return ScopeAdministration.SetAssignmentAsync(c, session, layla, ClientC, true, "x", null, ct);
        }));

        Assert.Empty(recorder.Commands);
    }

    [Fact]
    public async Task Layer2_Database_RefusesModeChange_WhenTheApplicationCheckIsBypassed()
    {
        var (khaled, alAmin, layla) = await SeedAsync();
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var session = new SessionContext(khaled, alAmin);

        var error = await Assert.ThrowsAsync<CriticalWriteException>(() => UnitOfWork.RunAsync(db, session, (c, ct) =>
            ScopeAdministration.WriteModeAsync(c, session, layla, "assigned", ct)));

        Assert.Equal(0, error.Affected);
        Assert.Equal(1, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'all'", ("m", layla)));
    }

    [Fact]
    public async Task Layer2_Database_RefusesAssignment_WhenTheApplicationCheckIsBypassed()
    {
        var (khaled, alAmin, layla) = await SeedAsync();
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var session = new SessionContext(khaled, alAmin);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => UnitOfWork.RunAsync(db, session, (c, ct) =>
            ScopeAdministration.WriteAssignmentAsync(c, session, layla, ClientC, true, "x", null, ct)));

        Assert.Contains(Chain(error), e => e is PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege });
        Assert.Equal(0, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM scope_assignments WHERE membership_id = @m", ("m", layla)));
    }

    private async Task<(Guid Khaled, Guid AlAmin, Guid LaylaMembership)> SeedAsync() =>
        (await ScalarGuidAsync("SELECT id FROM users WHERE username = 'khaled'"),
         await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Al-Amin'"),
         await ScalarGuidAsync(
             "SELECT m.id FROM memberships m JOIN users u ON u.id = m.user_id JOIN tenants t ON t.id = m.tenant_id " +
             "WHERE u.username = 'layla' AND t.name = 'Al-Amin'"));

    private async Task<Guid> ScalarGuidAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No row: {sql}"));
    }

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}

using Api.Endpoints;
using Core.Data;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.3 T4.13 [W] (the owner's decision: the API refusal is [B], in Conformance; the absence of any database
// command is [W], here) — each member-management operation checks core.members.manage from the resolved scope before
// the database: for Khaled (Al-Amin, operator, no core.members.manage), every operation is refused with no command
// sent after the unit of work's own context statements. Nothing is written.
[Collection(WhiteBoxCollection.Name)]
public class T4_MemberAdministrationTests(WhiteBoxFixture fixture)
{
    public static TheoryData<string> Operations => new() { "status", "add-role", "remove-role", "invite", "revoke" };

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task T4_13_Layer1_RefusesBeforeAnyCommand(string operation)
    {
        var khaled = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'khaled'");
        var alAmin = await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Al-Amin'");
        var layla = await ScalarGuidAsync(
            "SELECT m.id FROM memberships m JOIN users u ON u.id = m.user_id WHERE u.username = 'layla' AND m.tenant_id = @t", ("t", alAmin));
        var viewer = await ScalarGuidAsync("SELECT id FROM roles WHERE tenant_id = @t AND code = 'viewer'", ("t", alAmin));
        var recorder = new CommandRecorder();
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);
        var session = new SessionContext(khaled, alAmin);

        var error = await Assert.ThrowsAsync<NotPermittedException>(() => UnitOfWork.RunAsync(db, session, (c, ct) =>
        {
            recorder.Clear();
            return operation switch
            {
                "status" => MemberAdministration.SetStatusAsync(c, layla, "disabled", ct),
                "add-role" => MemberAdministration.AddRoleAsync(c, session, layla, viewer, ct),
                "remove-role" => MemberAdministration.RemoveRoleAsync(c, layla, viewer, ct),
                "invite" => MemberAdministration.InviteAsync(c, session, "t4.13@white-box.test", viewer, "assigned", ct),
                _ => MemberAdministration.RevokeAsync(c, Guid.CreateVersion7(), ct),
            };
        }));

        Assert.Equal("core.members.manage", error.Permission);
        Assert.Empty(recorder.Commands);
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
}

using Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.2 T3.1 [W] — PLATFORM_CORE §3.7 Test 26, written before any T3 code (PROOF_SPEC: "Start with
// Test 26 before any code. Write it, watch it fail, then build."). Against the seed contract (§7): Khaled
// (assigned, no management), Sara (all, admin), Rami (assigned, manager through a custom role).
[Collection(WhiteBoxCollection.Name)]
public class T3_Test26(WhiteBoxFixture fixture)
{
    public static TheoryData<string> Usernames => new() { "khaled", "sara", "rami" };

    public static TheoryData<string, string, bool, bool> Members => new()
    {
        { "khaled", "assigned", false, false },
        { "sara",   "all",      true,  true },
        { "rami",   "assigned", false, true },
    };

    // Resolution with a single join query, before app.membership_id is set → zero rows, so Rule 7 throws:
    // the failure is loud, not a silent default. The transaction is opened by hand with app.user_id and
    // app.tenant_id only — the state before step a — since the unit of work itself now resolves in order.
    [Theory]
    [MemberData(nameof(Usernames))]
    public async Task Test26_SingleJoin_BeforeMembershipId_ReturnsZero_AndRule7IsLoud(string username)
    {
        var (user, tenant) = await UserAndAlAminAsync(username);
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.user_id', {user.ToString("D")}, true)");
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.tenant_id', {tenant.ToString("D")}, true)");

        var rows = await ((from m in db.Memberships
             join s in db.MembershipScopes on m.Id equals s.MembershipId
             where m.UserId == user && m.TenantId == tenant
             select new
             {
                 m.Id,
                 s.ScopeMode,
                 CanManage = (from mr in db.MembershipRoles
                              join rp in db.RolePermissions on mr.RoleId equals rp.RoleId
                              join p in db.Permissions on rp.PermissionId equals p.Id
                              where mr.MembershipId == m.Id && p.Code == "core.scope.manage"
                              select 1).Any(),
             }).ToListAsync());
        await transaction.RollbackAsync();

        Assert.Empty(rows);
        Assert.Throws<MissingMembershipScopeException>(() =>
            ScopeResolver.RequireScopeRow(rows.Select(r => r.ScopeMode).SingleOrDefault(), Guid.Empty));
    }

    // The mandatory order (§3.5/6, steps a-b-c) → succeeds, with each member's real values.
    [Theory]
    [MemberData(nameof(Members))]
    public async Task Test26_ThreeSteps_Succeed(string username, string mode, bool scopeAll, bool canManage)
    {
        var (user, tenant) = await UserAndAlAminAsync(username);
        var expectedMembership = await MembershipAsync(username);
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        var (resolved, settings) = await UnitOfWork.RunAsync(db, new SessionContext(user, tenant), async (c, ct) =>
        {
            var r = await ScopeResolver.ResolveAsync(c, user, tenant, ct);
            var s = await c.Database.SqlQueryRaw<string>(
                "SELECT current_setting('app.membership_id', true) || '|' || current_setting('app.scope_all', true) || '|' || " +
                "current_setting('app.can_manage_scope', true) AS \"Value\"").SingleAsync(ct);
            return (r, s);
        });

        Assert.Equal(new ResolvedScope(expectedMembership, scopeAll, canManage), resolved);
        Assert.Equal($"{expectedMembership}|{(scopeAll ? "true" : "false")}|{(canManage ? "true" : "false")}", settings);
        Assert.Equal(mode == "all", resolved.ScopeAll);
    }

    // The guard: three separate reads, each followed by the SET LOCAL it allows, in the order a-b-c. Any change
    // that reverts resolution to a single query fails here before it can be merged.
    [Fact]
    public async Task Test26_Guard_ThreeReadsInOrder_EachFollowedByItsSetLocal()
    {
        var (user, tenant) = await UserAndAlAminAsync("rami");
        await using var dataSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);

        await UnitOfWork.RunAsync(db, new SessionContext(user, tenant), async (c, ct) =>
        {
            recorder.Clear();
            await ScopeResolver.ResolveAsync(c, user, tenant, ct);
        });

        var commands = recorder.Commands;
        Assert.Equal(6, commands.Count);
        Assert.Contains("FROM memberships", commands[0]);
        Assert.DoesNotContain("membership_scope", commands[0]);
        Assert.StartsWith("SET LOCAL app.membership_id = ", commands[1]);
        Assert.Contains("FROM membership_scope", commands[2]);
        Assert.StartsWith("SET LOCAL app.scope_all = ", commands[3]);
        Assert.Contains("FROM membership_roles", commands[4]);
        Assert.Contains("permissions", commands[4]);
        Assert.StartsWith("SET LOCAL app.can_manage_scope = ", commands[5]);
    }

    // Rule 7 through the resolver: a membership with no membership_scope row → the resolver throws
    // (the loud upper layer), while the read beneath it returned zero rows silently.
    [Fact]
    public async Task Test26_Rule7_MembershipWithoutScopeRow_ResolverThrows()
    {
        var layla = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'layla'");
        var maan = await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Maan'");
        var membership = Guid.CreateVersion7();
        await ExecuteAsMigratorAsync(
            "INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES (@m, @t, @u, 'active', now())",
            ("m", membership), ("t", maan), ("u", layla));
        try
        {
            await using var dataSource = fixture.CreateAppUserDataSource();
            await using var db = WhiteBoxFixture.Context(dataSource);

            var error = await Assert.ThrowsAsync<MissingMembershipScopeException>(() =>
                UnitOfWork.RunAsync(db, new SessionContext(layla, maan), (c, ct) => ScopeResolver.ResolveAsync(c, layla, maan, ct)));
            Assert.Equal(membership, error.MembershipId);
        }
        finally
        {
            await ExecuteAsMigratorAsync("DELETE FROM memberships WHERE id = @m", ("m", membership));
        }
    }

    private async Task<(Guid User, Guid Tenant)> UserAndAlAminAsync(string username) =>
        (await ScalarGuidAsync("SELECT id FROM users WHERE username = @a", ("a", username)),
         await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Al-Amin'"));

    private Task<Guid> MembershipAsync(string username) =>
        ScalarGuidAsync(
            "SELECT m.id FROM memberships m JOIN users u ON u.id = m.user_id JOIN tenants t ON t.id = m.tenant_id " +
            "WHERE u.username = @a AND t.name = 'Al-Amin'", ("a", username));

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

using System.Net;
using Npgsql;

namespace Conformance;

// PROOF_SPEC v1.2 T3 — the [B] criteria that change nothing in the seed: Test 7 (T3.2), Test 27's memberships
// part (T3.7), and Test 17-c (OPEN_ITEMS 11). Through SQL as the functional roles, and through HTTP.
public class T3_LoginTests
{
    // ---- T3.2 [B] — Test 7: authenticator resolves the credential with no tenant context.

    [Fact]
    public async Task T3_2_Test7_Authenticator_ResolvesCredential_WithNoContext()
    {
        var omar = await Seed.UserAsync("omar");
        await using var session = await Session.OpenAsync(Target.Authenticator);

        var rows = await session.ListAsync<Guid>(
            "SELECT u.id FROM users u JOIN user_password_credentials c ON c.user_id = u.id WHERE u.username = @u",
            ("u", "omar"));

        Assert.Equal([omar], rows);
    }

    [Fact]
    public async Task T3_2_Test7_Login_Succeeds_WithNoTenantSelected()
    {
        using var browser = new Browser();

        var login = await browser.LoginAsync("omar", Browser.PasswordOf("omar"));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        // Logged in, no tenant yet: the tenant-scoped surface refuses explicitly rather than showing emptiness.
        var (status, body) = await browser.GetAsync("/me/scope");
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("no_active_tenant", body.GetProperty("error").GetString());
    }

    // Test 7, second half: app_user with app.user_id alone sees only their own memberships.
    [Fact]
    public async Task T3_2_Test7_AppUser_WithUserIdAlone_SeesOnlyOwnMemberships()
    {
        var omar = await Seed.UserAsync("omar");
        var total = await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships");
        await using var session = await Session.OpenAsync(Target.AppUser, user: omar);

        var owners = await session.ListAsync<Guid>("SELECT user_id FROM memberships");

        Assert.Equal(2, owners.Count);
        Assert.All(owners, owner => Assert.Equal(omar, owner));
        Assert.True(total > owners.Count, "the seed has other people's memberships to hide");
    }

    [Fact]
    public async Task T3_2_Test7_TenantList_ShowsOnlyOwnTenants()
    {
        using var browser = await Browser.SignedInAsync("omar");

        var (status, body) = await browser.GetAsync("/tenants");

        Assert.Equal(HttpStatusCode.OK, status);
        var names = body.GetProperty("tenants").EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Equal([Seed.AlAmin, Seed.Maan], names.Order().ToList());
    }

    // Tenant selection admits a member only: a tenant with no membership for this user → refused.
    [Fact]
    public async Task T3_TenantSelection_NotAMember_IsRefused()
    {
        using var browser = await Browser.SignedInAsync("khaled");

        var response = await browser.SelectAsync(await Seed.TenantAsync(Seed.Maan));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("not_a_member", (await browser.JsonAsync(response)).GetProperty("error").GetString());
    }

    // ---- T3.7 [B] — Test 27, the memberships part: on the same connection, all six variables set (v1.16), the
    // transaction ends (COMMIT or ROLLBACK), optionally DISCARD ALL, then app.user_id alone (the
    // tenant-selection path) → the caller's own memberships only, with no error.

    public enum Ending { Commit, Rollback }

    [Theory]
    [InlineData(Ending.Commit, false)]
    [InlineData(Ending.Commit, true)]
    [InlineData(Ending.Rollback, false)]
    [InlineData(Ending.Rollback, true)]
    public async Task T3_7_Test27_Memberships_UserIdAlone_ReusedConnection_OwnRowsNoError(Ending ending, bool discardAll)
    {
        var omar = await Seed.UserAsync("omar");
        await using var connection = await Target.OpenUnpooledAsync(Target.AppUser);

        await using (var first = await connection.BeginTransactionAsync())
        {
            await SetLocalAsync(connection, first, "app.user_id", (await Seed.UserAsync("sara")).ToString());
            await SetLocalAsync(connection, first, "app.tenant_id", (await Seed.TenantAsync(Seed.AlAmin)).ToString());
            await SetLocalAsync(connection, first, "app.membership_id", (await Seed.MembershipAsync("sara", Seed.AlAmin)).ToString());
            await SetLocalAsync(connection, first, "app.scope_all", "true");
            await SetLocalAsync(connection, first, "app.can_manage_scope", "true");
            await SetLocalAsync(connection, first, "app.can_manage_members", "true");
            Assert.NotEmpty(await MembershipOwnersAsync(connection, first));

            if (ending == Ending.Commit) await first.CommitAsync();
            else await first.RollbackAsync();
        }

        if (discardAll)
        {
            await using var discard = new NpgsqlCommand("DISCARD ALL", connection);
            await discard.ExecuteNonQueryAsync();
        }

        await using var second = await connection.BeginTransactionAsync();
        await SetLocalAsync(connection, second, "app.user_id", omar.ToString());
        var owners = await MembershipOwnersAsync(connection, second);

        Assert.Equal(2, owners.Count);
        Assert.All(owners, owner => Assert.Equal(omar, owner));
    }

    // ---- OPEN_ITEMS 11 — Test 17-c [B]: a scope variable from a header, the query string, or the payload →
    // no effect. Khaled (assigned, no management) stays assigned, and cannot manage.

    [Fact]
    public async Task Test17c_ScopeFromHeaderOrQuery_HasNoEffect()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var scope = await khaled.ScopeAsync(request =>
        {
            request.RequestUri = new Uri("/me/scope?scope_all=true&app.scope_all=true&can_manage_scope=true", UriKind.Relative);
            request.Headers.Add("X-Scope-All", "true");
            request.Headers.Add("X-App-Scope-All", "true");
            request.Headers.Add("app.scope_all", "true");
            request.Headers.Add("X-Can-Manage-Scope", "true");
            request.Headers.Add("X-Membership-Id", Guid.CreateVersion7().ToString());
        });

        Assert.Equal("assigned", scope.Mode);
        Assert.False(scope.CanManage);
    }

    [Fact]
    public async Task Test17c_ScopeFromPayload_HasNoEffect()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);
        var layla = await Seed.MembershipAsync("layla", Seed.AlAmin);

        var (status, _) = await khaled.PutAsync($"/scope/memberships/{layla}/mode",
            new { scope_mode = "assigned", scope_all = true, can_manage_scope = true, membership_id = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'all'", ("m", layla)));
    }

    // Test 17-a at the API (OPEN_ITEMS 12): an admin changing their own mode → the database's silent zero
    // rows become an explicit error through the rows-affected guard, and the row is untouched.
    [Fact]
    public async Task Test17a_Api_AdminChangingOwnMode_IsLoud()
    {
        using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin);
        var own = await Seed.MembershipAsync("sara", Seed.AlAmin);

        var (status, body) = await sara.PutAsync($"/scope/memberships/{own}/mode", new { scope_mode = "assigned" });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("not_permitted", body);
        Assert.Equal("all", (await sara.ScopeAsync()).Mode);
    }

    private static async Task SetLocalAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, string value)
    {
        await using var command = new NpgsqlCommand("SELECT set_config(@n, @v, true)", connection, transaction);
        command.Parameters.AddWithValue("n", name);
        command.Parameters.AddWithValue("v", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<Guid>> MembershipOwnersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand("SELECT user_id FROM memberships", connection, transaction);
        var rows = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetGuid(0));
        return rows;
    }
}

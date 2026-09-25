using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Conformance;

/// <summary>A person with an account and a membership in a <see cref="World"/>, signed in with that tenant selected.</summary>
public sealed class Member(Browser browser, string username, string email, string password, Guid userId, Guid membershipId) : IDisposable
{
    public Browser Browser { get; } = browser;
    public string Username { get; } = username;
    public string Email { get; } = email;
    public string Password { get; } = password;
    public Guid UserId { get; } = userId;
    public Guid MembershipId { get; } = membershipId;

    public void Dispose() => Browser.Dispose();
}

/// <summary>
/// A tenant of the test's own (PROOF_SPEC T4), created through the real path — POST /provision/tenants, registered
/// in Development and CI — never through the seed: the T4 tests commit what they do through the API, and the seed
/// contract's tenants must stay as §7 states them. Members join through the real invitation and acceptance paths.
/// Lookups by id run as migrator, outside every tenant context.
/// </summary>
public sealed class World : IDisposable
{
    private readonly List<Member> _members = [];

    private World(Guid tenantId, Member owner)
    {
        TenantId = tenantId;
        Owner = owner;
        _members.Add(owner);
    }

    public Guid TenantId { get; }
    public Member Owner { get; }

    public static string Tag() => Guid.CreateVersion7().ToString("N")[^12..];

    public static async Task<World> BootstrapAsync(string label)
    {
        var (username, email, password) = Account(label + "-owner");
        using var anonymous = new Browser();
        var response = await anonymous.Client.PostAsJsonAsync("/provision/tenants",
            new { tenant_name = $"T4 {label} {Tag()}", full_name = username, email, username, password });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await anonymous.JsonAsync(response);
        var tenant = body.GetProperty("tenant_id").GetGuid();
        var owner = await SignInAsync(username, email, password, tenant, body.GetProperty("user_id").GetGuid(),
            body.GetProperty("membership_id").GetGuid());
        return new World(tenant, owner);
    }

    public static (string Username, string Email, string Password) Account(string label)
    {
        var username = $"t4-{label}-{Tag()}";
        return (username, username + "@t4.test", username + "-password");
    }

    public Task<Guid> RoleAsync(string code) =>
        ScalarAsync("SELECT id FROM roles WHERE tenant_id = @t AND code = @c", ("t", TenantId), ("c", code));

    /// <summary>A custom role holding exactly the given permissions — test setup as migrator, like the seed's own.</summary>
    public async Task<string> CustomRoleAsync(params string[] permissions)
    {
        var code = "custom-" + Tag();
        await ExecuteAsMigratorAsync(
            """
            INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active) VALUES (uuidv7(), @t, @c, 'دور', 'Custom', false, true);
            INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
              SELECT uuidv7(), @t, r.id, p.id FROM roles r CROSS JOIN permissions p WHERE r.tenant_id = @t AND r.code = @c AND p.code = ANY (@p);
            """, ("t", TenantId), ("c", code), ("p", permissions));
        return code;
    }

    public async Task<(HttpResponseMessage Response, Guid InvitationId, string Token)> InviteAsync(Member by, string email, string roleCode,
        string mode)
    {
        var response = await by.Browser.Client.PostAsJsonAsync("/members/invitations",
            new { email, role_id = await RoleAsync(roleCode), intended_scope_mode = mode });
        if (response.StatusCode != HttpStatusCode.Created)
            return (response, Guid.Empty, "");
        var body = await by.Browser.JsonAsync(response);
        return (response, body.GetProperty("invitation_id").GetGuid(), body.GetProperty("token").GetString()!);
    }

    public static Task<HttpResponseMessage> AcceptNewAsync(Browser anonymous, Guid tenant, string token, string email, string username,
        string password) =>
        anonymous.Client.PostAsJsonAsync("/invitations/accept",
            new { tenant_id = tenant, token, email, full_name = username, username, password });

    public static Task<HttpResponseMessage> AcceptAsync(Browser signedIn, Guid tenant, string token) =>
        signedIn.Client.PostAsJsonAsync("/invitations/accept", new { tenant_id = tenant, token });

    /// <summary>A new person joins: invited by the owner, accepts as a new account, signs in, selects the tenant.</summary>
    public async Task<Member> JoinAsync(string label, string roleCode, string mode)
    {
        var (username, email, password) = Account(label);
        var (invited, _, token) = await InviteAsync(Owner, email, roleCode, mode);
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        using (var anonymous = new Browser())
            Assert.Equal(HttpStatusCode.NoContent, (await AcceptNewAsync(anonymous, TenantId, token, email, username, password)).StatusCode);
        var user = await ScalarAsync("SELECT id FROM users WHERE username = @u", ("u", username));
        var membership = await ScalarAsync("SELECT id FROM memberships WHERE tenant_id = @t AND user_id = @u", ("t", TenantId), ("u", user));
        var member = await SignInAsync(username, email, password, TenantId, user, membership);
        _members.Add(member);
        return member;
    }

    public Task<long> ActiveOwnersAsync() => Seed.CountAsMigratorAsync(
        """
        SELECT count(*) FROM memberships m WHERE m.tenant_id = @t AND m.status = 'active'
          AND EXISTS (SELECT 1 FROM membership_roles mr JOIN roles r ON r.id = mr.role_id
                      WHERE mr.membership_id = m.id AND r.code = 'owner' AND r.is_system)
        """, ("t", TenantId));

    public Task<long> ActiveAllAsync() => Seed.CountAsMigratorAsync(
        """
        SELECT count(*) FROM memberships m JOIN membership_scope s ON s.membership_id = m.id
        WHERE m.tenant_id = @t AND m.status = 'active' AND s.scope_mode = 'all'
        """, ("t", TenantId));

    public static async Task<string> StatusOfAsync(Guid membership) =>
        (string)(await ScalarObjectAsync("SELECT status FROM memberships WHERE id = @m", ("m", membership)))!;

    public static async Task<Guid> ScalarAsync(string sql, params (string Name, object Value)[] parameters) =>
        (Guid)(await ScalarObjectAsync(sql, parameters) ?? throw new InvalidOperationException($"No row: {sql}"));

    public static async Task<object?> ScalarObjectAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return await command.ExecuteScalarAsync();
    }

    public static async Task ExecuteAsMigratorAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Starts every request at once, behind one gate, and returns their statuses in order.</summary>
    public static async Task<HttpStatusCode[]> ConcurrentlyAsync(params Func<Task<HttpResponseMessage>>[] requests)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = requests.Select(async request =>
        {
            await gate.Task;
            return (await request()).StatusCode;
        }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(running);
    }

    private static async Task<Member> SignInAsync(string username, string email, string password, Guid tenant, Guid user, Guid membership)
    {
        var browser = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.LoginAsync(username, password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.SelectAsync(tenant)).StatusCode);
        return new Member(browser, username, email, password, user, membership);
    }

    public void Dispose()
    {
        foreach (var member in _members)
            member.Dispose();
    }
}

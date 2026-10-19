using Core.Data;
using Core.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Ops;

namespace Core.WhiteBoxTests;

// PLATFORM_CORE Amendment 01 §5.4, §2.11 — the operations procedure's create-tenant, the provisioner's third path:
// a tenant from the templates and an owner invitation with no inviter, nothing else; the owner accepted through the
// existing path; and the guard in front of src/Ops. Each test creates a tenant of its own, as T4_9's real path does.
[Collection(WhiteBoxCollection.Name)]
public class T4_CreateTenantTests(WhiteBoxFixture fixture)
{
    private const string Origin = "https://platform.example.test";

    private static CreateTenantRequest Request(string label)
    {
        var tag = Guid.CreateVersion7().ToString("N")[^12..];
        return new CreateTenantRequest($"wb ops {label} {tag}", $"wb-ops-{label}-{tag}@white-box.test");
    }

    // ---- What it writes: the tenant, roles = templates with their permissions, and one pending owner invitation with
    // no inviter in scope 'all' — and no person, user or membership. Every write audited as Bootstrap's are.
    [Fact]
    public async Task CreateTenant_WritesTheTenantItsRolesAndOneBootstrapInvitation_AndNoIdentity()
    {
        await using var dataSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        await using var db = ProvisionerContext(dataSource);
        var request = Request("writes");

        var result = await CreateTenant.RunAsync(db, request);

        var t = ("t", (object)result.TenantId);
        Assert.Equal(1, await fixture.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE id = @t AND name = @n AND status = 'active'",
            t, ("n", request.TenantName)));
        Assert.Equal(await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_templates"),
            await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE tenant_id = @t AND is_system", t));
        Assert.Equal(await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_template_permissions"),
            await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_permissions WHERE tenant_id = @t", t));
        Assert.Equal(1, await fixture.CountAsMigratorAsync(
            """
            SELECT count(*) FROM invitations i JOIN roles r ON r.tenant_id = i.tenant_id AND r.id = i.role_id
            WHERE i.tenant_id = @t AND i.id = @i AND i.invited_by IS NULL AND i.status = 'pending' AND i.intended_scope_mode = 'all'
              AND r.code = 'owner' AND i.email = @e AND i.token_hash = @h
            """,
            t, ("i", result.InvitationId), ("e", request.OwnerEmail), ("h", InvitationToken.Hash(result.Token))));
        Assert.Equal(1, await fixture.CountAsMigratorAsync("SELECT count(*) FROM invitations WHERE tenant_id = @t", t));
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t", t));
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM persons WHERE email = @e", ("e", request.OwnerEmail)));

        // Audited in its own transaction (§5.4, §4.4/4), with the actor Bootstrap writes: provisioner, no user.
        foreach (var entity in new[] { "tenants", "roles", "role_permissions", "invitations" })
            Assert.True(await fixture.CountAsMigratorAsync(
                "SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_type = @x AND actor_type = 'provisioner' AND actor_id IS NULL",
                t, ("x", entity)) > 0, $"no audit entry for {entity}");
    }

    // ---- The templates, unreadable as zero rows: loud, and no tenant — the same guard as bootstrap (T4.9).
    [Fact]
    public async Task CreateTenant_ZeroTemplates_Loud_NothingCreated()
    {
        await using var dataSource = fixture.CreateMigratorDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var request = Request("zero");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM role_template_permissions; DELETE FROM role_templates;");

        await Assert.ThrowsAsync<RoleTemplatesUnavailableException>(() => CreateTenant.InTransactionAsync(db, request, CancellationToken.None));
        Assert.Equal(0, await db.Tenants.CountAsync(x => x.Name == request.TenantName));
        await transaction.RollbackAsync();
    }

    // ---- The owner accepts through the existing acceptance path, which creates the person, user and membership:
    // an active owner in scope 'all', the invitation accepted. The invitation is read back as an entity with a NULL
    // inviter — the materialization that a non-nullable InvitedBy would refuse. And Test 28c on the whole path: no
    // command carries RETURNING.
    [Fact]
    public async Task TheBootstrapInvitation_AcceptedThroughTheExistingPath_MakesAnActiveOwner_NoReturning()
    {
        var recorder = new CommandRecorder();
        await using var dataSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        var request = Request("accept");
        await using var creating = ProvisionerContext(dataSource, recorder);
        var created = await CreateTenant.RunAsync(creating, request);

        var username = "wb-ops-" + Guid.CreateVersion7().ToString("N")[^12..];
        await using var accepting = ProvisionerContext(dataSource, recorder);
        var accepted = await Acceptance.RunAsync(accepting, new AcceptRequest(created.TenantId, created.Token,
            new NewAccount(request.OwnerEmail, "Owner", username, "wb-ops-password")), null);

        Assert.False(accepted.Returned);
        Assert.Equal(1, await fixture.CountAsMigratorAsync(
            """
            SELECT count(*) FROM memberships m
            JOIN membership_roles mr ON mr.tenant_id = m.tenant_id AND mr.membership_id = m.id
            JOIN roles r ON r.tenant_id = mr.tenant_id AND r.id = mr.role_id AND r.code = 'owner'
            JOIN membership_scope s ON s.tenant_id = m.tenant_id AND s.membership_id = m.id AND s.scope_mode = 'all'
            WHERE m.tenant_id = @t AND m.id = @m AND m.user_id = @u AND m.status = 'active'
            """,
            ("t", created.TenantId), ("m", accepted.MembershipId), ("u", accepted.UserId)));
        Assert.Equal(1, await fixture.CountAsMigratorAsync("SELECT count(*) FROM invitations WHERE id = @i AND status = 'accepted'",
            ("i", created.InvitationId)));
        Assert.Contains(recorder.Commands, c => c.Contains("INSERT INTO invitations ", StringComparison.Ordinal));
        Assert.DoesNotContain(recorder.Commands, c => c.Contains("RETURNING", StringComparison.OrdinalIgnoreCase));
    }

    // ---- src/Ops end to end, in-process: the printed link is the interface's acceptance link on the configured
    // origin, and its token is the invitation's.
    [Fact]
    public async Task Ops_CreateTenant_PrintsTheAcceptanceLink_OnTheConfiguredOrigin()
    {
        var request = Request("ops");
        var (code, stdout, stderr) = await RunOpsAsync(Configuration(origin: Origin + "/"),
            "create-tenant", "--name", request.TenantName, "--owner-email", request.OwnerEmail);

        Assert.True(code == 0, stderr);
        var link = stdout.Split('\n').Single(l => l.StartsWith(Origin, StringComparison.Ordinal)).Trim();
        var match = System.Text.RegularExpressions.Regex.Match(link,
            "^" + System.Text.RegularExpressions.Regex.Escape(Origin) + "/app/invitations/accept#tenant=([0-9a-f-]{36})&token=([A-Za-z0-9_-]{43})$");
        Assert.True(match.Success, link);
        Assert.Equal(1, await fixture.CountAsMigratorAsync(
            "SELECT count(*) FROM invitations i JOIN tenants t ON t.id = i.tenant_id WHERE t.id = @t AND t.name = @n AND i.token_hash = @h",
            ("t", Guid.Parse(match.Groups[1].Value)), ("n", request.TenantName), ("h", InvitationToken.Hash(match.Groups[2].Value))));
    }

    // ---- The guard: a connection string Ops may not hold refuses it before anything runs — under the migrator key,
    // under an unknown key, or as migrator under an allowed key. Each is seen to create nothing.
    public static TheoryData<string, string> Refused => new()
    {
        { "migrator", "Host=localhost;Username=migrator;Password=x" },
        { "app_user", "Host=localhost;Username=app_user;Password=x" },
        { "authenticator", "Host=localhost;Username=authenticator;Password=x" },
        { "provisioner", "Host=localhost;Username=migrator;Password=x" },
        { "job_runner", "Host=localhost;User ID=MIGRATOR;Password=x" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Ops_RefusesToRun_WithAConnectionStringItMayNotHold(string key, string value)
    {
        var request = Request("guard");
        var configuration = Configuration(origin: Origin, extra: new() { [$"ConnectionStrings:{key}"] = value });

        var (code, stdout, stderr) = await RunOpsAsync(configuration, "create-tenant", "--name", request.TenantName, "--owner-email", request.OwnerEmail);

        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Contains(key, stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", request.TenantName)));
    }

    // The guard admits what §5.4 allows: provisioner and job_runner, as themselves.
    [Fact]
    public void Ops_Guard_AdmitsProvisionerAndJobRunner()
    {
        Assert.Null(OpsCommand.RefusedConnections(Configuration(origin: Origin,
            extra: new() { ["ConnectionStrings:job_runner"] = "Host=localhost;Username=job_runner;Password=x" })));
    }

    // ---- Ops:PublicOrigin: unset, or not an origin → loud, and nothing created. Never a bare path to guess from.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("platform.example.test")]
    [InlineData("https://platform.example.test/app")]
    [InlineData("ftp://platform.example.test")]
    public async Task Ops_CreateTenant_WithoutAPublicOrigin_Loud_NothingCreated(string? origin)
    {
        var request = Request("origin");
        var (code, stdout, stderr) = await RunOpsAsync(Configuration(origin),
            "create-tenant", "--name", request.TenantName, "--owner-email", request.OwnerEmail);

        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Contains("Ops:PublicOrigin", stderr);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", request.TenantName)));
    }

    // ---- Usage: what §5.4 names and is not built (--plan) is refused by name; anything else is a usage error.
    [Theory]
    [InlineData("--plan", "trial")]
    [InlineData("--owner", "x")]
    [InlineData("--name")]
    public async Task Ops_CreateTenant_BadArguments_Loud_NothingCreated(params string[] extra)
    {
        var request = Request("usage");
        var (code, stdout, stderr) = await RunOpsAsync(Configuration(Origin),
            ["create-tenant", "--name", request.TenantName, "--owner-email", request.OwnerEmail, .. extra]);

        Assert.Equal(1, code);
        Assert.Equal("", stdout);
        Assert.Contains(extra[0] == "--plan" ? "OPEN_ITEMS 89" : "Usage", stderr);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", request.TenantName)));
    }

    private static IConfiguration Configuration(string? origin, Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:provisioner"] = WhiteBoxFixture.ConnectionString("provisioner"),
            ["Ops:PublicOrigin"] = origin,
        };
        foreach (var (key, value) in extra ?? [])
            values[key] = value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunOpsAsync(IConfiguration configuration, params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await OpsCommand.RunAsync(args, configuration, stdout, stderr);
        return (code, stdout.ToString(), stderr.ToString());
    }

    private static ProvisionerDbContext ProvisionerContext(NpgsqlDataSource dataSource, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
    {
        var options = new DbContextOptionsBuilder<ProvisionerDbContext>();
        options.UseCoreDataAccess(dataSource);
        if (extra.Length > 0)
            options.AddInterceptors(extra);
        return new ProvisionerDbContext(options.Options);
    }
}

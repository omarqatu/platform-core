using System.Net;
using System.Net.Http.Json;
using Api.Endpoints;
using Core.Data;
using Core.Provisioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.3 T4.9, T4.10 and T4.17 [W] — the provisioner's paths (PLATFORM_CORE v1.16 §4.4, §3.9, §3.10).
[Collection(WhiteBoxCollection.Name)]
public class T4_ProvisioningTests(WhiteBoxFixture fixture)
{
    private static BootstrapRequest Request(string label)
    {
        var tag = Guid.CreateVersion7().ToString("N")[^12..];
        return new BootstrapRequest($"wb {label} {tag}", "Owner", $"wb-{label}-{tag}@white-box.test", $"wb-{label}-{tag}", "wb-password");
    }

    // ---- T4.9 — template-derived inserts are critical writes; without read access to the templates → a loud error.

    // No read grant (app_user holds none on role_templates): the read fails loudly, before any write.
    [Fact]
    public async Task T4_9_WithoutReadAccessToTheTemplates_Loud_NothingCreated()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var request = Request("t4-9-grant");
        await using var transaction = await db.Database.BeginTransactionAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => Bootstrap.InTransactionAsync(db, request, CancellationToken.None));
        await transaction.RollbackAsync();

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        Assert.Contains("role_templates", error.MessageText);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", request.TenantName)));
    }

    // Zero rows — what a read policy returns to a reader it does not admit — is not a tenant with no roles: loud.
    // The templates are removed inside a migrator transaction that is rolled back.
    [Fact]
    public async Task T4_9_ZeroTemplates_Loud_NotASilentZero()
    {
        await using var dataSource = fixture.CreateMigratorDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var request = Request("t4-9-zero");
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM role_template_permissions; DELETE FROM role_templates;");

        await Assert.ThrowsAsync<RoleTemplatesUnavailableException>(() => Bootstrap.InTransactionAsync(db, request, CancellationToken.None));
        Assert.Equal(0, await db.Tenants.CountAsync(t => t.Name == request.TenantName));
        await transaction.RollbackAsync();

        Assert.True(await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_templates") > 0);
    }

    // The real path, as provisioner: roles created = templates, each with the template's permissions, all system roles.
    [Fact]
    public async Task T4_9_RolesCreated_EqualTheTemplates()
    {
        await using var dataSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        await using var db = ProvisionerContext(dataSource);

        var result = await Bootstrap.RunAsync(db, Request("t4-9-real"));

        Assert.Equal(await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_templates"),
            await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE tenant_id = @t AND is_system", ("t", result.TenantId)));
        Assert.Equal(await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_template_permissions"),
            await fixture.CountAsMigratorAsync("SELECT count(*) FROM role_permissions WHERE tenant_id = @t", ("t", result.TenantId)));
    }

    // ---- T4.10 — Test 28c on the full path: bootstrap and invitation acceptance (a new account, and a return) through
    // real EF commands → zero commands containing RETURNING.

    [Fact]
    public async Task T4_10_Test28c_BootstrapAndAcceptance_NoReturning()
    {
        var recorder = new CommandRecorder();
        await using var provisionerSource = CoreDataAccess.CreateDataSource(WhiteBoxFixture.ConnectionString("provisioner"));
        await using var appUserSource = fixture.CreateAppUserDataSource();

        await using var provisioner = ProvisionerContext(provisionerSource, recorder);
        var boot = await Bootstrap.RunAsync(provisioner, Request("t4-10"));
        var owner = new SessionContext(boot.UserId, boot.TenantId);
        var viewer = await ScalarGuidAsync("SELECT id FROM roles WHERE tenant_id = @t AND code = 'viewer'", ("t", boot.TenantId));

        await using var app = WhiteBoxFixture.Context(appUserSource, recorder);
        var email = $"wb-t4-10-{Guid.CreateVersion7():N}@white-box.test";
        var invited = await UnitOfWork.RunAsync(app, owner, (c, ct) => MemberAdministration.InviteAsync(c, owner, email, viewer, "assigned", ct));
        await using var accepting = ProvisionerContext(provisionerSource, recorder);
        var accepted = await Acceptance.RunAsync(accepting, new AcceptRequest(boot.TenantId, invited.Token,
            new NewAccount(email, "Invitee", "wb-t4-10-" + Guid.CreateVersion7().ToString("N")[^12..], "wb-password")), null);

        // The invitee leaves, and returns by a new invitation (3.10): the return path's commands too.
        var member = new SessionContext(accepted.UserId, boot.TenantId);
        await using (var leaving = WhiteBoxFixture.Context(appUserSource, recorder))
            await UnitOfWork.RunAsync(leaving, member, (c, ct) => MemberAdministration.LeaveAsync(c, ct));
        await using (var inviting = WhiteBoxFixture.Context(appUserSource, recorder))
            invited = await UnitOfWork.RunAsync(inviting, owner, (c, ct) => MemberAdministration.InviteAsync(c, owner, email, viewer, "all", ct));
        await using var returning = ProvisionerContext(provisionerSource, recorder);
        var returned = await Acceptance.RunAsync(returning, new AcceptRequest(boot.TenantId, invited.Token, null), accepted.UserId);

        Assert.True(returned.Returned);
        var commands = recorder.Commands;
        foreach (var table in new[] { "tenants", "roles", "role_permissions", "persons", "users", "user_password_credentials",
                     "memberships", "membership_roles", "membership_scope", "membership_auth", "invitations", "audit_log" })
            Assert.Contains(commands, c => c.Contains($"INSERT INTO {table} ", StringComparison.Ordinal));
        foreach (var table in new[] { "memberships", "membership_scope", "invitations" })
            Assert.Contains(commands, c => c.Contains($"UPDATE {table} ", StringComparison.Ordinal));
        Assert.Contains(commands, c => System.Text.RegularExpressions.Regex.IsMatch(c, @"DELETE FROM membership_roles\s"));
        Assert.DoesNotContain(commands, c => c.Contains("RETURNING", StringComparison.OrdinalIgnoreCase));
    }

    // ---- T4.17 — POST /provision/tenants is registered in Development and CI only.

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public Task T4_17_Bootstrap_NotRegistered_InProductionOrStaging(string environment) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(environment);
            using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            var request = Request("t4-17");

            var response = await client.PostAsJsonAsync("/provision/tenants", new
            {
                tenant_name = request.TenantName, full_name = request.FullName, email = request.Email, username = request.Username,
                password = request.Password,
            });

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(BootstrapRoutes(api.Services.GetRequiredService<EndpointDataSource>()));
            Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", request.TenantName)));
        });

    [Theory]
    [InlineData("Development")]
    [InlineData("CI")]
    public Task T4_17_Bootstrap_Registered_InDevelopmentAndCI(string environment) =>
        InProcessApi.WithoutMigratorVariableAsync(async () =>
        {
            await using var api = InProcessApi.Create(environment);
            using var client = api.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

            var response = await client.PostAsJsonAsync("/provision/tenants", new { tenant_name = "" });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Single(BootstrapRoutes(api.Services.GetRequiredService<EndpointDataSource>()));
        });

    // Seen failing when the guard is removed: the same detector, on an app mapped in Production with the guard
    // replaced by one that admits everything, finds the route — and with the real guard, does not.
    [Fact]
    public void T4_17_TheDetector_FindsTheRoute_WhenTheGuardIsRemoved()
    {
        foreach (var (guard, expected) in new (Func<string, bool>?, int)[] { (null, 0), (_ => true, 1) })
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.Services.AddDbContext<ProvisionerDbContext>(options => options.UseNpgsql("Host=unused"));
            var app = builder.Build();

            var registered = app.MapBootstrap(app.Environment.EnvironmentName, guard);

            Assert.Equal(expected == 1, registered);
            Assert.Equal(expected, BootstrapRoutes(new CompositeEndpointDataSource(((IEndpointRouteBuilder)app).DataSources)).Count);
        }
    }

    private static List<string> BootstrapRoutes(EndpointDataSource endpoints) =>
        endpoints.Endpoints.OfType<RouteEndpoint>().Select(e => e.RoutePattern.RawText ?? "")
            .Where(p => p.TrimStart('/') == "provision/tenants").ToList();

    private static ProvisionerDbContext ProvisionerContext(NpgsqlDataSource dataSource, params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] extra)
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
}

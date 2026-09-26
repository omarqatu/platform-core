using System.Text.Json;
using Npgsql;

namespace Conformance;

// PROOF_SPEC v1.3 T7 — background jobs (PLATFORM_CORE v1.16 §8): the worker takes the active tenants from job_runner
// and processes each in an independent app_user transaction with the system context (8/5): app.scope_all = true,
// app.membership_id unset, app.can_manage_scope = false, app.can_manage_members = false. The trial job counts the
// subscriptions ending within 30 days and writes one audit line per tenant. Seed: Al-Amin's 20 subscriptions end on
// 2027-01-02…06, Maan's 6 likewise, so as of 2026-12-15 every one of them is within 30 days.
public class T7_BackgroundJobTests
{
    private static readonly DateOnly AsOf = new(2026, 12, 15);

    // ---- T7.1 — the job sees every subscription of the tenant, with no assignment.
    [Fact]
    public async Task T7_1_TheJobSeesEverySubscription_WithNoAssignment()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var maan = await Seed.TenantAsync(Seed.Maan);
        var suspended = await Seed.TenantAsync("Suspended");

        var run = await Worker.ExpiringSubscriptionsAsync(AsOf);

        var tenants = Tenants(run);
        Assert.Equal(20, tenants[alAmin].GetProperty("expiring").GetInt64());
        Assert.Equal(6, tenants[maan].GetProperty("expiring").GetInt64());
        Assert.False(tenants.ContainsKey(suspended));
        var runId = run.Summary!.Value.GetProperty("run_id").GetGuid();
        Assert.Equal(20, await AuditCountAsync(alAmin, runId));
        Assert.Equal(6, await AuditCountAsync(maan, runId));

        // Beneath it: the system context sees every subscription with no assignment — and no membership at all.
        await using var session = await SystemSessionAsync(alAmin);
        Assert.Equal(20, await session.CountAsync("SELECT count(*) FROM subscriptions"));
        Assert.Equal(4, await session.CountAsync("SELECT count(*) FROM clients"));
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM scope_assignments"));
    }

    // ---- T7.2 — the job attempts to write scope_assignments → fails: it manages no scope (can_manage_scope = false),
    // and no member.
    [Fact]
    public async Task T7_2_TheJobCannotWriteScopeAssignments()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        await using var session = await SystemSessionAsync(alAmin);

        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active) VALUES (@id, @t, @m, @c, 'contributor', true)",
            ("id", Guid.CreateVersion7()), ("t", alAmin), ("m", khaled), ("c", await Seed.ClientAsync(Seed.AlAmin, "C"))));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE scope_assignments SET active = false"));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'assigned'"));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE memberships SET status = 'disabled' WHERE id = @m", ("m", khaled)));
    }

    // ---- T7.3 — a failure processing one tenant → the other tenants complete, with no context leakage. The failure is
    // planted as migrator: a trigger refusing the audit line of one tenant of the test's own, dropped afterwards.
    [Fact]
    public async Task T7_3_OneTenantFails_TheOthersComplete_NoContextLeaks()
    {
        using var failing = await World.BootstrapAsync("t7-3-fail");
        using var other = await World.BootstrapAsync("t7-3-other");
        var p = await other.ClientAsync("P");
        await other.SubscriptionsAsync(p, AsOf.AddDays(1), AsOf.AddDays(29), AsOf.AddDays(31));
        var q = await failing.ClientAsync("Q");
        await failing.SubscriptionsAsync(q, AsOf.AddDays(2));
        var plant = "t7_plant_" + World.Tag();
        await World.ExecuteAsMigratorAsync(
            $"CREATE FUNCTION {plant}() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN " +
            $"IF NEW.tenant_id = '{failing.TenantId}' AND NEW.actor_type = 'job' THEN RAISE EXCEPTION 'T7.3 planted failure'; END IF; RETURN NEW; END $$; " +
            $"CREATE TRIGGER {plant} BEFORE INSERT ON audit_log FOR EACH ROW EXECUTE FUNCTION {plant}();");
        Worker.Run run;
        try
        {
            run = await Worker.ExpiringSubscriptionsAsync(AsOf);
        }
        finally
        {
            await World.ExecuteAsMigratorAsync($"DROP TRIGGER IF EXISTS {plant} ON audit_log; DROP FUNCTION IF EXISTS {plant}();");
        }

        Assert.Equal(2, run.ExitCode);
        var tenants = Tenants(run);
        Assert.Equal("failed", tenants[failing.TenantId].GetProperty("status").GetString());
        var runId = run.Summary!.Value.GetProperty("run_id").GetGuid();
        Assert.Null(await AuditCountAsync(failing.TenantId, runId));
        // The others completed, each with its own tenant's count: nothing of the failing transaction carried over.
        Assert.All(tenants.Where(t => t.Key != failing.TenantId), t => Assert.Equal("ok", t.Value.GetProperty("status").GetString()));
        Assert.Equal(2, tenants[other.TenantId].GetProperty("expiring").GetInt64());
        Assert.Equal(2, await AuditCountAsync(other.TenantId, runId));
        Assert.Equal(20, await AuditCountAsync(await Seed.TenantAsync(Seed.AlAmin), runId));
        Assert.Equal(0, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM audit_log WHERE new_value ->> 'run_id' = @r AND entity_id <> tenant_id", ("r", runId.ToString())));
    }

    // ---- T7.4 — job_runner reads from tenants only the active ones, and reads no other table.
    [Fact]
    public async Task T7_4_JobRunner_ReadsActiveTenantsOnly_AndNoOtherTable()
    {
        var active = await Seed.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE status = 'active'");
        await using var session = await Session.OpenAsync(Target.JobRunner);

        Assert.Equal(active, await session.CountAsync("SELECT count(*) FROM tenants"));
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM tenants WHERE status <> 'active'"));
        await using (var catalog = await Target.OpenAsync(Target.Migrator))
        await using (var tables = new NpgsqlCommand(
            "SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relname <> 'tenants'",
            catalog))
            foreach (var table in await Target.QueryStringsAsync(tables))
                Assert.Equal("42501", await session.SqlStateOfAsync($"SELECT 1 FROM {table} LIMIT 1"));
        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE tenants SET name = name"));
    }

    // The worker holds exactly its two connection strings: given another, it refuses to run (spec item k).
    [Fact]
    public async Task TheWorker_RefusesAForeignConnectionString()
    {
        var run = await Worker.RunAsync(["expiring-subscriptions"], new()
        {
            ["job_runner"] = Target.ConnectionString(Target.JobRunner),
            ["app_user"] = Target.ConnectionString(Target.AppUser),
            ["migrator"] = Target.ConnectionString(Target.Migrator),
        });

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("migrator", run.Error);
    }

    // ---- helpers

    private static Dictionary<Guid, JsonElement> Tenants(Worker.Run run)
    {
        Assert.True(run.Summary is not null, run.Error);
        return run.Summary!.Value.GetProperty("tenants").EnumerateArray().ToDictionary(t => t.GetProperty("tenant_id").GetGuid());
    }

    // The count in this run's audit line for the tenant, or null when there is none.
    private static async Task<long?> AuditCountAsync(Guid tenant, Guid runId)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(
            "SELECT (new_value ->> 'count')::bigint FROM audit_log WHERE tenant_id = @t AND entity_id = @t AND actor_type = 'job' AND new_value ->> 'run_id' = @r",
            connection);
        command.Parameters.AddWithValue("t", tenant);
        command.Parameters.AddWithValue("r", runId.ToString());
        return await command.ExecuteScalarAsync() is long count ? count : null;
    }

    // The system context of 8/5, set by hand as a job transaction sets it: no user, no membership.
    private static async Task<Session> SystemSessionAsync(Guid tenant)
    {
        var session = await Session.OpenAsync(Target.AppUser, tenant: tenant, scopeAll: true, canManageScope: false, canManageMembers: false);
        return session;
    }
}

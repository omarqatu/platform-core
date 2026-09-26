using System.Diagnostics;
using System.Globalization;
using System.Text;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Modules.Subscriptions;
using Npgsql;

// PROOF_SPEC v1.3 T8 — the measurement (spec item j). The threshold, written in the spec before the measurement:
//   The reference query: a sorted list of 50 subscriptions, for an assigned member assigned to 20 clients, in a tenant
//   with 500 clients and 20,000 subscriptions.
//   Acceptance: p95 for the query under both policies (tenant_isolation + client_scope) ≤ 1.5 × p95 for the same
//   query under tenant_isolation alone, under 20 concurrent requests.
// The query runs through the actual stack: the app_user role, the unit of work with its per-transaction resolution
// (3.5/6), and the module's own EF query (GET /subscriptions: ordered by id, 50 rows). "tenant_isolation alone" is
// client_scope on subscriptions neutralized as migrator (USING (true) WITH CHECK (true)) for the second phase, then
// restored from the template; its deparse is compared before and after.
// Usage: Measurement <output-directory>   (environment: MEASURE_APP_USER, MEASURE_MIGRATOR connection strings)

const int Clients = 500, SubscriptionsPerClient = 40, Assigned = 20, PageSize = 50;
const int Concurrency = 20, Warmup = 200, PerWorker = 100;

var output = args.Length > 0 ? args[0] : ".";
var appUser = Environment.GetEnvironmentVariable("MEASURE_APP_USER") ?? "Host=localhost;Port=5432;Database=platform;Username=app_user;Password=app_user_dev";
var migrator = Environment.GetEnvironmentVariable("MEASURE_MIGRATOR") ?? "Host=localhost;Port=5432;Database=platform;Username=migrator;Password=migrator_dev";
var poolSize = Concurrency + 5;
await using var dataSource = CoreDataAccess.CreateDataSource(new NpgsqlConnectionStringBuilder(appUser) { MaxPoolSize = poolSize }.ConnectionString);
var options = new DbContextOptionsBuilder<SubscriptionsDbContext>();
options.UseCoreDataAccess(dataSource);

// ---- The dataset, as migrator (as the seed is written): a tenant of its own, its base roles, an operator member
// with scope 'assigned' and 20 assignments; 500 clients, 40 subscriptions each.
var tenant = Guid.CreateVersion7();
var user = Guid.CreateVersion7();
await Migrator($"""
    DO $$
    DECLARE t uuid := '{tenant}'; u uuid := '{user}'; p uuid := uuidv7(); m uuid := uuidv7();
    BEGIN
      INSERT INTO tenants (id, name, status, created_at) VALUES (t, 'T8 measurement ' || t, 'active', now());
      INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
        SELECT uuidv7(), t, rt.code, rt.name_ar, rt.name_en, true, true FROM role_templates rt;
      INSERT INTO role_permissions (id, tenant_id, role_id, permission_id)
        SELECT uuidv7(), t, r.id, rtp.permission_id FROM roles r
        JOIN role_templates rt ON rt.code = r.code JOIN role_template_permissions rtp ON rtp.template_id = rt.id WHERE r.tenant_id = t;
      INSERT INTO persons (id, full_name, email, created_at) VALUES (p, 'T8 member', 't8-' || u || '@measure.test', now());
      INSERT INTO users (id, person_id, user_type, username, status) VALUES (u, p, 'employee', 't8-' || u, 'active');
      INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES (m, t, u, 'active', now());
      INSERT INTO membership_roles (id, tenant_id, membership_id, role_id)
        SELECT uuidv7(), t, m, r.id FROM roles r WHERE r.tenant_id = t AND r.code = 'operator';
      INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode) VALUES (uuidv7(), t, m, 'assigned');
      INSERT INTO membership_auth (id, tenant_id, membership_id, provider) VALUES (uuidv7(), t, m, 'password');
      CREATE TEMP TABLE t8_clients ON COMMIT DROP AS SELECT uuidv7() AS id, n FROM generate_series(1, {Clients}) n;
      INSERT INTO clients (id, tenant_id, scope_ref_id, name, created_at) SELECT id, t, id, 'Client ' || n, now() FROM t8_clients;
      INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at)
        SELECT uuidv7(), t, c.id, 'Service ' || k, DATE '2027-01-01' + ((c.n * 7 + k) % 365), now()
        FROM t8_clients c CROSS JOIN generate_series(1, {SubscriptionsPerClient}) k;
      -- 20 assigned clients, spread across the tenant (every 25th).
      INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active, reason)
        SELECT uuidv7(), t, m, c.id, 'contributor', true, 'T8 measurement' FROM t8_clients c WHERE c.n % {Clients / Assigned} = 0;
    END $$;
    ANALYZE clients; ANALYZE subscriptions; ANALYZE scope_assignments;
    """);
var session = new SessionContext(user, tenant);

var policyBefore = await PolicyTextAsync();
var withBoth = await PhaseAsync("both policies (tenant_isolation + client_scope)");
var planBoth = await ExplainAsync();
string planAlone;
Phase alone;
await Migrator("ALTER POLICY client_scope ON subscriptions USING (true) WITH CHECK (true)");
try
{
    alone = await PhaseAsync("tenant_isolation alone (client_scope neutralized)");
    planAlone = await ExplainAsync();
}
finally
{
    await Migrator(RestoreClientScope());
}
var policyAfter = await PolicyTextAsync();
if (policyAfter != policyBefore)
    throw new InvalidOperationException("client_scope on subscriptions was not restored to its text.");

// ---- The report.
var ratio = withBoth.QueryP95 / alone.QueryP95;
var verdict = ratio <= 1.5 ? "PASS" : "EXCEEDED";
Directory.CreateDirectory(output);
var report = new StringBuilder();
report.AppendLine("# T8 — the measurement (PROOF_SPEC v1.3, spec item j)");
report.AppendLine();
report.AppendLine("**The threshold (written in the spec before the measurement, not adjusted):** p95 of the reference query under both policies ≤ 1.5 × p95 under `tenant_isolation` alone, under 20 concurrent requests.");
report.AppendLine();
report.AppendLine($"**Verdict: {verdict}** — p95 ratio {ratio:0.000} (both {withBoth.QueryP95:0.000} ms / alone {alone.QueryP95:0.000} ms; the limit is 1.5).");
report.AppendLine();
report.AppendLine("## The setup");
report.AppendLine();
report.AppendLine($"- Measured: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC. PostgreSQL: `{await ScalarAsync("SELECT version()")}`. .NET {Environment.Version}, {Environment.ProcessorCount} logical CPUs; the database and the client on the same host.");
report.AppendLine($"- The tenant: {Clients} clients, {Clients * SubscriptionsPerClient:N0} subscriptions ({SubscriptionsPerClient} per client). The member: operator, scope `assigned`, assigned to {Assigned} clients ({Assigned * SubscriptionsPerClient} subscriptions visible).");
report.AppendLine($"- The reference query: the module's `GET /subscriptions` query — `subscriptions` ordered by `id`, {PageSize} rows — through EF, as `app_user`, inside the unit of work (its resolution a-b-c and SET LOCALs run first, 3.5/6).");
report.AppendLine($"- Load: {Concurrency} concurrent workers, {Warmup} warm-up iterations per phase (discarded), then {PerWorker} iterations per worker = {Concurrency * PerWorker} samples per phase. Pool size {poolSize}.");
report.AppendLine("- \"Query\" is the query alone (the stopwatch around its `ToListAsync`); \"transaction\" is the whole unit of work (begin, context, resolution, query, commit).");
report.AppendLine("- `tenant_isolation` alone: `client_scope` on `subscriptions` neutralized as migrator (`USING (true) WITH CHECK (true)`) for the second phase, then restored from the template; its deparse compared equal before and after.");
report.AppendLine();
report.AppendLine("## Raw numbers (milliseconds)");
report.AppendLine();
report.AppendLine("| Phase | Measure | n | min | p50 | p90 | p95 | p99 | max | mean |");
report.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
foreach (var phase in new[] { withBoth, alone })
{
    report.AppendLine(Row(phase.Name, "query", phase.Query));
    report.AppendLine(Row(phase.Name, "transaction", phase.Transaction));
}
report.AppendLine();
report.AppendLine($"Transaction p95 ratio (for information, not the criterion): {Percentile(withBoth.Transaction, 95) / Percentile(alone.Transaction, 95):0.000}.");
report.AppendLine();
report.AppendLine("## The plans (EXPLAIN ANALYZE, one execution in the member's context)");
report.AppendLine();
report.AppendLine("Both policies:");
report.AppendLine("```");
report.AppendLine(planBoth);
report.AppendLine("```");
report.AppendLine();
report.AppendLine("`tenant_isolation` alone:");
report.AppendLine("```");
report.AppendLine(planAlone);
report.AppendLine("```");
report.AppendLine();
report.AppendLine("Every sample is in `T8_samples.csv` (phase, measure, milliseconds).");
await File.WriteAllTextAsync(Path.Combine(output, "T8_report.md"), report.ToString());
await File.WriteAllLinesAsync(Path.Combine(output, "T8_samples.csv"),
    new[] { "phase,measure,ms" }.Concat(new[] { withBoth, alone }.SelectMany(p =>
        p.Query.Select(v => $"{(p == withBoth ? "both" : "alone")},query,{v.ToString("0.0000", CultureInfo.InvariantCulture)}")
            .Concat(p.Transaction.Select(v => $"{(p == withBoth ? "both" : "alone")},transaction,{v.ToString("0.0000", CultureInfo.InvariantCulture)}")))));
Console.WriteLine(report);
await Migrator($"UPDATE memberships SET status = 'disabled' WHERE tenant_id = '{tenant}'");   // the dataset stays, inert
return verdict == "PASS" ? 0 : 3;

// ---- helpers

async Task<Phase> PhaseAsync(string name)
{
    for (var i = 0; i < Warmup; i++)
        await OnceAsync();
    var query = new List<double>[Concurrency];
    var transaction = new List<double>[Concurrency];
    await Task.WhenAll(Enumerable.Range(0, Concurrency).Select(async w =>
    {
        query[w] = new List<double>(PerWorker);
        transaction[w] = new List<double>(PerWorker);
        for (var i = 0; i < PerWorker; i++)
        {
            var (q, t) = await OnceAsync();
            query[w].Add(q);
            transaction[w].Add(t);
        }
    }));
    return new Phase(name, query.SelectMany(x => x).ToList(), transaction.SelectMany(x => x).ToList());
}

async Task<(double Query, double Transaction)> OnceAsync()
{
    await using var db = new SubscriptionsDbContext(options.Options);
    var whole = Stopwatch.StartNew();
    var elapsed = await UnitOfWork.RunAsync(db, session, async (c, ct) =>
    {
        var watch = Stopwatch.StartNew();
        var rows = await c.Subscriptions.OrderBy(s => s.Id).Take(PageSize).Select(s => new { s.Id, s.ScopeRefId, s.ServiceName, s.EndsOn }).ToListAsync(ct);
        watch.Stop();
        if (rows.Count != PageSize)
            throw new InvalidOperationException($"The reference query returned {rows.Count} rows, not {PageSize}.");
        return watch.Elapsed.TotalMilliseconds;
    });
    whole.Stop();
    return (elapsed, whole.Elapsed.TotalMilliseconds);
}

async Task<string> ExplainAsync()
{
    await using var db = new SubscriptionsDbContext(options.Options);
    // The reference query's own text, as EF generates it: its parameter lines dropped, the limit inlined.
    var generated = db.Subscriptions.OrderBy(s => s.Id).Take(PageSize).Select(s => new { s.Id, s.ScopeRefId, s.ServiceName, s.EndsOn }).ToQueryString();
    var sql = string.Join(' ', generated.Split('\n').Where(l => !l.StartsWith("--", StringComparison.Ordinal))).Replace("@p", PageSize.ToString()).Trim();
    // The text is EF's own for a constant query; nothing external reaches it.
#pragma warning disable EF1003
    return await UnitOfWork.RunAsync(db, session, async (c, ct) =>
        sql + "\n\n" + string.Join('\n', await c.Database.SqlQueryRaw<string>("EXPLAIN (ANALYZE, COSTS, BUFFERS) " + sql).ToListAsync(ct)));
#pragma warning restore EF1003
}

async Task Migrator(string sql)
{
    await using var connection = new NpgsqlConnection(migrator);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
    await command.ExecuteNonQueryAsync();
}

async Task<string> ScalarAsync(string sql)
{
    await using var connection = new NpgsqlConnection(migrator);
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand(sql, connection);
    return (string)(await command.ExecuteScalarAsync())!;
}

Task<string> PolicyTextAsync() => ScalarAsync(
    "SELECT pg_get_expr(polqual, polrelid) || ' | ' || pg_get_expr(polwithcheck, polrelid) || ' | ' || polpermissive::text FROM pg_policy WHERE polrelid = 'subscriptions'::regclass AND polname = 'client_scope'");

static string RestoreClientScope()
{
    const string condition = """
        COALESCE((SELECT NULLIF(current_setting('app.scope_all', true), '')::boolean), false)
        OR EXISTS (
          SELECT 1 FROM scope_assignments sa
          WHERE sa.tenant_id     = subscriptions.tenant_id
            AND sa.scope_ref_id  = subscriptions.scope_ref_id
            AND sa.membership_id = (SELECT NULLIF(current_setting('app.membership_id', true), '')::uuid)
            AND sa.active)
        """;
    return $"ALTER POLICY client_scope ON subscriptions USING ({condition}) WITH CHECK ({condition})";
}

static double Percentile(List<double> values, double p)
{
    var sorted = values.Order().ToList();
    var rank = p / 100 * (sorted.Count - 1);
    var low = (int)Math.Floor(rank);
    var high = (int)Math.Ceiling(rank);
    return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
}

static string Row(string phase, string measure, List<double> v) =>
    string.Create(CultureInfo.InvariantCulture,
        $"| {phase} | {measure} | {v.Count} | {v.Min():0.000} | {Percentile(v, 50):0.000} | {Percentile(v, 90):0.000} | {Percentile(v, 95):0.000} | {Percentile(v, 99):0.000} | {v.Max():0.000} | {v.Average():0.000} |");

sealed record Phase(string Name, List<double> Query, List<double> Transaction)
{
    public double QueryP95 { get; } = Percentile(Query);
    private static double Percentile(List<double> values)
    {
        var sorted = values.Order().ToList();
        var rank = 0.95 * (sorted.Count - 1);
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return sorted[low] + (sorted[high] - sorted[low]) * (rank - low);
    }
}

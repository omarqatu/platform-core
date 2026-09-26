using System.Text.Json;
using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Modules.Subscriptions;

// The background-job worker (PROOF_SPEC T7, PLATFORM_CORE 8). One run of one job, then exit; the schedule is the
// scheduler's (8: it throttles the rate). Usage:
//   Worker expiring-subscriptions [--as-of yyyy-MM-dd]
// Prints one JSON summary line; exit code 0 when every tenant completed, 2 when any failed, 1 on a usage error.

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .AddCommandLine(args.Skip(1).ToArray(), new Dictionary<string, string> { ["--as-of"] = "AsOf" })
    .Build();

// Exactly two connection strings: job_runner and app_user. Any other refuses to run (spec item k, as Api's guard).
string[] allowed = ["job_runner", "app_user"];
var foreign = configuration.GetSection("ConnectionStrings").GetChildren().Select(c => c.Key)
    .Except(allowed, StringComparer.OrdinalIgnoreCase).ToArray();
if (foreign.Length > 0)
{
    Console.Error.WriteLine($"The worker must not hold these connection strings: {string.Join(", ", foreign)}.");
    return 1;
}
foreach (var name in allowed)
{
    if (string.IsNullOrWhiteSpace(configuration.GetConnectionString(name)))
    {
        Console.Error.WriteLine($"ConnectionStrings:{name} is not configured.");
        return 1;
    }
}

if (args is not ["expiring-subscriptions", ..])
{
    Console.Error.WriteLine("Usage: Worker expiring-subscriptions [--as-of yyyy-MM-dd]");
    return 1;
}
var asOf = configuration["AsOf"] is { } text ? DateOnly.ParseExact(text, "yyyy-MM-dd") : DateOnly.FromDateTime(DateTime.UtcNow);
var cap = configuration.GetValue("Jobs:TenantBatchCap", SystemContext.DefaultBatchCap);

using var loggers = LoggerFactory.Create(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));
await using var jobRunnerSource = CoreDataAccess.CreateDataSource(configuration.GetConnectionString("job_runner")!);
await using var appUserSource = CoreDataAccess.CreateDataSource(configuration.GetConnectionString("app_user")!);

List<Guid> tenants;
var jobRunnerOptions = new DbContextOptionsBuilder<CoreDbContext>();
jobRunnerOptions.UseCoreDataAccess(jobRunnerSource);
await using (var jobRunner = new CoreDbContext(jobRunnerOptions.Options))
    tenants = await SystemContext.ActiveTenantsAsync(jobRunner, CancellationToken.None);

var runId = Guid.CreateVersion7();
var appUserOptions = new DbContextOptionsBuilder<SubscriptionsDbContext>();
appUserOptions.UseCoreDataAccess(appUserSource);
var results = await SystemContext.FanOutAsync(tenants, cap, () => new SubscriptionsDbContext(appUserOptions.Options),
    (db, tenant, ct) => ExpiringSubscriptionsJob.RunForTenantAsync(db, tenant, asOf, runId, ct),
    loggers.CreateLogger("Worker"), CancellationToken.None);

Console.WriteLine(JsonSerializer.Serialize(new
{
    run_id = runId,
    job = ExpiringSubscriptionsJob.Action,
    as_of = asOf,
    tenants = results.Select(r => new { tenant_id = r.TenantId, status = r.Status, expiring = r.Value }),
}));
return results.Any(r => r.Status != "ok") ? 2 : 0;

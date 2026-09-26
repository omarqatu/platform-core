using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Conformance;

/// <summary>
/// The implementation's background-job worker (from T7), run as its own process: the command comes from configuration
/// (PROOF_SPEC 6 — another implementation names its own), given the job_runner and app_user connection strings only.
/// Its contract: `expiring-subscriptions --as-of yyyy-MM-dd` prints one JSON summary line (run_id, and per tenant its
/// status and count) and writes one audit line per tenant carrying the run_id.
/// </summary>
public static class Worker
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddEnvironmentVariables()
        .Build();

    public sealed record Run(int ExitCode, JsonElement? Summary, string Error);

    public static Task<Run> ExpiringSubscriptionsAsync(DateOnly asOf) =>
        RunAsync(["expiring-subscriptions", "--as-of", asOf.ToString("yyyy-MM-dd")],
            new() { ["job_runner"] = Target.ConnectionString(Target.JobRunner), ["app_user"] = Target.ConnectionString(Target.AppUser) });

    public static async Task<Run> RunAsync(string[] arguments, Dictionary<string, string> connectionStrings)
    {
        var start = new ProcessStartInfo(Config["Worker:Command"] ?? throw new InvalidOperationException("Worker:Command is not configured."))
        {
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (Config["Worker:Arguments"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Concat(arguments))
            start.ArgumentList.Add(argument);
        // Only what is passed here reaches the worker: every inherited connection string is removed first (CI puts
        // migrator's in the environment of the test run).
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("ConnectionStrings__", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        foreach (var (name, value) in connectionStrings)
            start.Environment["ConnectionStrings__" + name] = value;

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var line = (await output).Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return new Run(process.ExitCode, line is null ? null : JsonDocument.Parse(line).RootElement.Clone(), await error);
    }
}

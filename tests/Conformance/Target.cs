using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Conformance;

// The implementation under test, reached only through SQL (and, from T3, HTTP).
// Connection strings and the role-name mapping come from configuration, so the
// suite runs unmodified against any implementation (PROOF_SPEC 6).
public static class Target
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddEnvironmentVariables()
        .Build();

    public const string Migrator = "migrator";
    public const string AppUser = "app_user";
    public const string Authenticator = "authenticator";
    public const string JobRunner = "job_runner";
    public const string Provisioner = "provisioner";

    public static readonly string[] AllRoles = [Migrator, AppUser, Authenticator, JobRunner, Provisioner];
    public static readonly string[] PrivilegedRoles = [Migrator, Provisioner];

    // The database role name that plays a functional role (identity mapping by default).
    public static string RoleName(string role) =>
        Config[$"RoleNames:{role}"] ?? throw new InvalidOperationException($"RoleNames:{role} is not configured.");

    public static async Task<NpgsqlConnection> OpenAsync(string role)
    {
        var connectionString = Config.GetConnectionString(role)
            ?? throw new InvalidOperationException($"ConnectionStrings:{role} is not configured.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task<List<string>> QueryStringsAsync(NpgsqlCommand command)
    {
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetString(0));
        return rows;
    }
}

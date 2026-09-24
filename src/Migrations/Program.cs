using Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

// The migration tool: the only holder of the migrator connection string
// (PROOF_SPEC T0, spec item k). Never invoked from application startup.
var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

var connectionString = config.GetConnectionString("migrator")
    ?? throw new InvalidOperationException("ConnectionStrings:migrator is not configured.");

// Migrations run under migrator alone — refuse any other role.
await using (var connection = new NpgsqlConnection(connectionString))
{
    await connection.OpenAsync();
    await using var command = new NpgsqlCommand("SELECT current_user", connection);
    var currentUser = (string?)await command.ExecuteScalarAsync();
    if (currentUser != "migrator")
        throw new InvalidOperationException($"Migrations must run as migrator, not '{currentUser}'.");
}

var options = new DbContextOptionsBuilder<CoreDbContext>()
    .UseNpgsql(connectionString, npgsql => npgsql
        .MigrationsAssembly(typeof(Program).Assembly.GetName().Name)
        // Outside public: Check 5 requires RLS on every public table, with an empty exemption list.
        .MigrationsHistoryTable("__EFMigrationsHistory", "migrations_meta"))
    // The schema is written as SQL (Migrations/Sql), not diffed from the EF model,
    // so the model never "matches" a migration snapshot. Only the SQL is authoritative.
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
    .Options;

await using var db = new CoreDbContext(options);
await db.Database.MigrateAsync();

var applied = await db.Database.GetAppliedMigrationsAsync();
Console.WriteLine($"Migration complete as migrator. Applied migrations: {applied.Count()}.");

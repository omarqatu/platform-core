using Core;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Core.WhiteBoxTests;

/// <summary>
/// Two tenants of the white-box tests' own, W1 and W2, created as migrator on the real schema (since T2)
/// and deleted afterwards: W1 has 25 custom roles and one system role, W2 has 5 custom roles. Fresh
/// random ids, unrelated to the seed contract; every key and timestamp written explicitly (§2).
/// </summary>
public sealed class WhiteBoxFixture : IAsyncLifetime
{
    public const int W1CustomRoles = 25;
    public const int W2CustomRoles = 5;

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddEnvironmentVariables()
        .Build();

    public Guid W1 { get; } = Guid.CreateVersion7();
    public Guid W2 { get; } = Guid.CreateVersion7();
    public Guid W1SystemRole { get; } = Guid.CreateVersion7();
    public IReadOnlyList<Guid> W2RoleIds { get; private set; } = [];

    public string AppUserConnectionString { get; } = Config.GetConnectionString("app_user")
        ?? throw new InvalidOperationException("ConnectionStrings:app_user is not configured.");

    public string MigratorConnectionString { get; } = Config.GetConnectionString("migrator")
        ?? throw new InvalidOperationException("ConnectionStrings:migrator is not configured.");

    /// <summary>An app_user data source through Core, with a small pool so connections are reused.</summary>
    public NpgsqlDataSource CreateAppUserDataSource(int maxPoolSize = 8) =>
        CoreDataAccess.CreateDataSource(
            new NpgsqlConnectionStringBuilder(AppUserConnectionString) { MaxPoolSize = maxPoolSize }.ConnectionString);

    public NpgsqlDataSource CreateMigratorDataSource() => CoreDataAccess.CreateDataSource(MigratorConnectionString);

    /// <summary>A Core context over a data source, with Core's transaction layer and any extra interceptors.</summary>
    public static CoreDbContext Context(NpgsqlDataSource dataSource, params IInterceptor[] extra)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>();
        options.UseCoreDataAccess(dataSource);
        if (extra.Length > 0)
            options.AddInterceptors(extra);
        return new CoreDbContext(options.Options);
    }

    public async Task InitializeAsync()
    {
        await ExecuteAsync(
            """
            INSERT INTO tenants (id, name, status, created_at) VALUES
              (@w1, 'White-box W1', 'active', now()), (@w2, 'White-box W2', 'active', now());
            INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
              SELECT uuidv7(), @w1, 'custom-' || lpad(n::text, 2, '0'), 'دور', 'Role', false, true FROM generate_series(1, @w1n) n;
            INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
              VALUES (@w1sys, @w1, 'owner', 'مالك', 'Owner', true, true);
            INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active)
              SELECT uuidv7(), @w2, 'custom-' || lpad(n::text, 2, '0'), 'دور', 'Role', false, true FROM generate_series(1, @w2n) n;
            """,
            ("w1", W1), ("w2", W2), ("w1sys", W1SystemRole), ("w1n", W1CustomRoles), ("w2n", W2CustomRoles));
        W2RoleIds = await QueryGuidsAsync("SELECT id FROM roles WHERE tenant_id = @w2 ORDER BY code", ("w2", W2));
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(
            """
            DELETE FROM audit_log WHERE tenant_id IN (@w1, @w2);
            DELETE FROM roles WHERE tenant_id IN (@w1, @w2);
            DELETE FROM tenants WHERE id IN (@w1, @w2);
            """,
            ("w1", W1), ("w2", W2));
        var left = await CountAsMigratorAsync(
            "SELECT (SELECT count(*) FROM tenants WHERE id IN (@w1, @w2)) + (SELECT count(*) FROM roles WHERE tenant_id IN (@w1, @w2))",
            ("w1", W1), ("w2", W2));
        if (left != 0)
            throw new InvalidOperationException($"White-box fixture left {left} row(s) behind.");
    }

    /// <summary>Reads as migrator (BYPASSRLS): the ground truth, outside any tenant context.</summary>
    public async Task<long> CountAsMigratorAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task<string> NameAsMigratorAsync(Guid roleId)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT name_en FROM roles WHERE id = @id", connection);
        command.Parameters.AddWithValue("id", roleId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<Guid>> QueryGuidsAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return ids;
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class WhiteBoxCollection : ICollectionFixture<WhiteBoxFixture>
{
    public const string Name = "white-box tenants";
}

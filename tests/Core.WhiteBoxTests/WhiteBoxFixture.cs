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
/// Since T3 each tenant has one real member — W1User, W2User: a person, a user, an active membership and its
/// membership_scope row with mode 'all', and no role — because the unit of work resolves the second axis on
/// every transaction with a user and a tenant (3.5/6), and a user with no membership there cannot enter it.
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
    public Guid W1User { get; } = Guid.CreateVersion7();
    public Guid W2User { get; } = Guid.CreateVersion7();
    public Guid W1Membership { get; } = Guid.CreateVersion7();
    public Guid W2Membership { get; } = Guid.CreateVersion7();
    public IReadOnlyList<Guid> W2RoleIds { get; private set; } = [];

    public string AppUserConnectionString { get; } = Config.GetConnectionString("app_user")
        ?? throw new InvalidOperationException("ConnectionStrings:app_user is not configured.");

    public string MigratorConnectionString { get; } = Config.GetConnectionString("migrator")
        ?? throw new InvalidOperationException("ConnectionStrings:migrator is not configured.");

    /// <summary>Any configured connection string by role name — for hosting Api in-process (T3).</summary>
    public static string ConnectionString(string role) => Config.GetConnectionString(role)
        ?? throw new InvalidOperationException($"ConnectionStrings:{role} is not configured.");

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
            INSERT INTO persons (id, full_name, email, phone_e164, created_at) VALUES
              (@p1, 'White-box W1 member', @u1::text || '@white-box.test', NULL, now()),
              (@p2, 'White-box W2 member', @u2::text || '@white-box.test', NULL, now());
            INSERT INTO users (id, person_id, user_type, username, status, language, theme, last_login_at) VALUES
              (@u1, @p1, 'employee', 'wb-' || @u1::text, 'active', NULL, NULL, NULL),
              (@u2, @p2, 'employee', 'wb-' || @u2::text, 'active', NULL, NULL, NULL);
            INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES
              (@m1, @w1, @u1, 'active', now()), (@m2, @w2, @u2, 'active', now());
            INSERT INTO membership_scope (id, tenant_id, membership_id, scope_mode) VALUES
              (uuidv7(), @w1, @m1, 'all'), (uuidv7(), @w2, @m2, 'all');
            """,
            ("w1", W1), ("w2", W2), ("w1sys", W1SystemRole), ("w1n", W1CustomRoles), ("w2n", W2CustomRoles),
            ("p1", Guid.CreateVersion7()), ("p2", Guid.CreateVersion7()), ("u1", W1User), ("u2", W2User),
            ("m1", W1Membership), ("m2", W2Membership));
        W2RoleIds = await QueryGuidsAsync("SELECT id FROM roles WHERE tenant_id = @w2 ORDER BY code", ("w2", W2));
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync(
            """
            DELETE FROM audit_log WHERE tenant_id IN (@w1, @w2);
            DELETE FROM membership_scope WHERE tenant_id IN (@w1, @w2);
            DELETE FROM memberships WHERE tenant_id IN (@w1, @w2);
            DELETE FROM users WHERE id IN (@u1, @u2);
            DELETE FROM persons WHERE email IN (@u1::text || '@white-box.test', @u2::text || '@white-box.test');
            DELETE FROM roles WHERE tenant_id IN (@w1, @w2);
            DELETE FROM tenants WHERE id IN (@w1, @w2);
            """,
            ("w1", W1), ("w2", W2), ("u1", W1User), ("u2", W2User));
        var left = await CountAsMigratorAsync(
            """
            SELECT (SELECT count(*) FROM tenants WHERE id IN (@w1, @w2)) + (SELECT count(*) FROM roles WHERE tenant_id IN (@w1, @w2))
                 + (SELECT count(*) FROM memberships WHERE tenant_id IN (@w1, @w2)) + (SELECT count(*) FROM users WHERE id IN (@u1, @u2))
                 + (SELECT count(*) FROM persons WHERE email IN (@u1::text || '@white-box.test', @u2::text || '@white-box.test'))
            """,
            ("w1", W1), ("w2", W2), ("u1", W1User), ("u2", W2User));
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

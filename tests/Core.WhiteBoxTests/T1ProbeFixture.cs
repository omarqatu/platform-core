using Core.Data;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Core.WhiteBoxTests;

/// <summary>
/// Creates the test-only t1_probe table (tests/fixtures/t1_probe.sql) as migrator
/// for the length of the collection, seeds two random tenants, and drops the
/// table afterwards. Conformance has the same fixture; a session advisory lock
/// keeps the two projects from creating the table at the same time.
/// </summary>
public sealed class T1ProbeFixture : IAsyncLifetime
{
    // Shared with Conformance's fixture.
    private const long ProbeLockKey = 0x7431_7072_6f62_65; // "t1probe"

    public const int TenantARows = 25;
    public const int TenantBRows = 5;

    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddEnvironmentVariables()
        .Build();

    public Guid TenantA { get; } = Guid.CreateVersion7();
    public Guid TenantB { get; } = Guid.CreateVersion7();
    public IReadOnlyList<Guid> TenantBRowIds { get; private set; } = [];

    public string AppUserConnectionString { get; } = Config.GetConnectionString("app_user")
        ?? throw new InvalidOperationException("ConnectionStrings:app_user is not configured.");

    private readonly string _migratorConnectionString = Config.GetConnectionString("migrator")
        ?? throw new InvalidOperationException("ConnectionStrings:migrator is not configured.");

    private NpgsqlConnection? _migrator;

    /// <summary>An app_user data source through Core, with a small pool so connections are reused.</summary>
    public NpgsqlDataSource CreateAppUserDataSource(int maxPoolSize = 8) =>
        CoreDataAccess.CreateDataSource(
            new NpgsqlConnectionStringBuilder(AppUserConnectionString) { MaxPoolSize = maxPoolSize }.ConnectionString);

    public async Task InitializeAsync()
    {
        _migrator = new NpgsqlConnection(_migratorConnectionString);
        await _migrator.OpenAsync();
        await ExecuteAsync("SELECT pg_advisory_lock(@key)", ("key", ProbeLockKey));
        await ExecuteAsync("DROP TABLE IF EXISTS public.t1_probe");
        await ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "t1_probe.sql")));
        await ExecuteAsync(
            """
            INSERT INTO t1_probe (tenant_id, label)
            SELECT @a, 'a-' || n FROM generate_series(1, @aRows) n
            UNION ALL
            SELECT @b, 'b-' || n FROM generate_series(1, @bRows) n
            """,
            ("a", TenantA), ("b", TenantB), ("aRows", TenantARows), ("bRows", TenantBRows));
        TenantBRowIds = await QueryGuidsAsync("SELECT id FROM t1_probe WHERE tenant_id = @b ORDER BY label", ("b", TenantB));
    }

    public async Task DisposeAsync()
    {
        if (_migrator is null)
            return;
        await ExecuteAsync("DROP TABLE IF EXISTS public.t1_probe");
        await ExecuteAsync("SELECT pg_advisory_unlock(@key)", ("key", ProbeLockKey));
        await _migrator.DisposeAsync();
    }

    /// <summary>Reads as migrator (BYPASSRLS): the ground truth, outside any tenant context.</summary>
    public async Task<int> CounterAsMigratorAsync(Guid id)
    {
        await using var command = new NpgsqlCommand("SELECT counter FROM t1_probe WHERE id = @id", _migrator);
        command.Parameters.AddWithValue("id", id);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    public async Task<long> CountLabelAsMigratorAsync(string label)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM t1_probe WHERE label = @label", _migrator);
        command.Parameters.AddWithValue("label", label);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<Guid>> QueryGuidsAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, _migrator);
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
        await using var command = new NpgsqlCommand(sql, _migrator);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class T1ProbeCollection : ICollectionFixture<T1ProbeFixture>
{
    public const string Name = "t1_probe";
}

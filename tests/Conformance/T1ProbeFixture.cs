using Npgsql;

namespace Conformance;

/// <summary>
/// Creates the test-only t1_probe table (tests/fixtures/t1_probe.sql) as migrator
/// for the length of the collection, seeds two random tenants, and drops the
/// table afterwards. The tenants are fresh random UUIDs, unrelated to the seed
/// contract in PROOF_SPEC §7 (project-owner decision, T1).
/// Core.WhiteBoxTests has the same fixture; a session advisory lock keeps the two
/// projects from creating the table at the same time when run in parallel.
/// </summary>
public sealed class T1ProbeFixture : IAsyncLifetime
{
    // Shared with Core.WhiteBoxTests' fixture.
    private const long ProbeLockKey = 0x7431_7072_6f62_65; // "t1probe"

    public const int TenantARows = 3;
    public const int TenantBRows = 5;

    public Guid TenantA { get; } = Guid.CreateVersion7();
    public Guid TenantB { get; } = Guid.CreateVersion7();

    private NpgsqlConnection? _migrator;

    public async Task InitializeAsync()
    {
        _migrator = await Target.OpenAsync(Target.Migrator);
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
    }

    public async Task DisposeAsync()
    {
        if (_migrator is null)
            return;
        await ExecuteAsync("DROP TABLE IF EXISTS public.t1_probe");
        await ExecuteAsync("SELECT pg_advisory_unlock(@key)", ("key", ProbeLockKey));
        await _migrator.DisposeAsync();
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

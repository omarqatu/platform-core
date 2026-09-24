using Npgsql;

namespace Conformance;

/// <summary>
/// The seed contract (PROOF_SPEC §7), found by name: tenant names and usernames. Never by an
/// internal id, so any implementation that satisfies the contract satisfies these lookups.
/// Lookups run as migrator, outside every tenant context.
/// </summary>
public static class Seed
{
    public const string AlAmin = "Al-Amin";
    public const string Maan = "Maan";

    public static Task<Guid> TenantAsync(string name) =>
        ScalarAsync("SELECT id FROM tenants WHERE name = @a", name);

    public static Task<Guid> UserAsync(string username) =>
        ScalarAsync("SELECT id FROM users WHERE username = @a", username);

    public static Task<Guid> MembershipAsync(string username, string tenant) =>
        ScalarAsync(
            "SELECT m.id FROM memberships m JOIN users u ON u.id = m.user_id JOIN tenants t ON t.id = m.tenant_id WHERE u.username = @a AND t.name = @b",
            username, tenant);

    public static Task<Guid> RoleAsync(string tenant, string code) =>
        ScalarAsync("SELECT r.id FROM roles r JOIN tenants t ON t.id = r.tenant_id WHERE t.name = @a AND r.code = @b", tenant, code);

    public static async Task<long> CountAsMigratorAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid> ScalarAsync(string sql, string a, string? b = null)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("a", a);
        if (b is not null)
            command.Parameters.AddWithValue("b", b);
        var value = await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException($"The seed contract has no row for {a}{(b is null ? "" : " / " + b)}.");
        return (Guid)value;
    }
}

/// <summary>
/// One transaction as a role, with the context variables set — what the transaction layer does
/// per request. Always rolled back on dispose, so no test changes the seed.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    public NpgsqlConnection Connection { get; }
    public NpgsqlTransaction Transaction { get; }

    private Session(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        Connection = connection;
        Transaction = transaction;
    }

    public static async Task<Session> OpenAsync(string role, Guid? user = null, Guid? tenant = null,
        Guid? membership = null, bool? scopeAll = null, bool? canManageScope = null)
    {
        var connection = await Target.OpenAsync(role);
        var transaction = await connection.BeginTransactionAsync();
        var session = new Session(connection, transaction);
        if (user is { } u) await session.SetAsync("app.user_id", u.ToString());
        if (tenant is { } t) await session.SetAsync("app.tenant_id", t.ToString());
        if (membership is { } m) await session.SetAsync("app.membership_id", m.ToString());
        if (scopeAll is { } s) await session.SetAsync("app.scope_all", s ? "true" : "false");
        if (canManageScope is { } c) await session.SetAsync("app.can_manage_scope", c ? "true" : "false");
        return session;
    }

    public async Task SetAsync(string variable, string value)
    {
        await using var command = new NpgsqlCommand("SELECT set_config(@n, @v, true)", Connection, Transaction);
        command.Parameters.AddWithValue("n", variable);
        command.Parameters.AddWithValue("v", value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long> CountAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    public async Task<List<T>> ListAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        var rows = new List<T>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(reader.GetFieldValue<T>(0));
        return rows;
    }

    public async Task<int> ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>Runs a statement expected to fail, in a savepoint so the session stays usable.</summary>
    public async Task<string> SqlStateOfAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await Transaction.SaveAsync("expect_error");
        try
        {
            await ExecuteAsync(sql, parameters);
            await Transaction.ReleaseAsync("expect_error");
            return "no error";
        }
        catch (PostgresException e)
        {
            await Transaction.RollbackAsync("expect_error");
            return e.SqlState;
        }
    }

    private NpgsqlCommand Command(string sql, (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, Connection, Transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command;
    }

    public async ValueTask DisposeAsync()
    {
        await Transaction.RollbackAsync();
        await Transaction.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

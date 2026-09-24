using System.Collections.Concurrent;
using Npgsql;

namespace Conformance;

// PROOF_SPEC T1.2 and T1.3 against t1_probe, a test-only table under the verbatim
// v1.10 tenant_isolation template. Harness proofs (project-owner decision, T1 PR):
// they are proven again against a real table after T2 is merged.
[Collection(T1ProbeCollection.Name)]
public class T1_FailSafeTests(T1ProbeFixture probe)
{
    // T1.2 [B] — without app.tenant_id → zero rows (Test 4), on a connection no
    // context has ever touched.
    [Fact]
    public async Task T1_2_NoTenantContext_FreshConnection_ZeroRows()
    {
        await using var connection = await Target.OpenUnpooledAsync(Target.AppUser);
        await using var transaction = await connection.BeginTransactionAsync();

        Assert.Empty(await VisibleTenantsAsync(connection, transaction));
    }

    // T1.2 — the non-vacuous half: with a tenant set, that tenant's rows and no others.
    [Fact]
    public async Task T1_2_WithTenantContext_SeesOnlyThatTenant()
    {
        await using var connection = await Target.OpenUnpooledAsync(Target.AppUser);
        await using var transaction = await connection.BeginTransactionAsync();
        await SetLocalAsync(connection, transaction, "app.tenant_id", probe.TenantA.ToString());

        var visible = await VisibleTenantsAsync(connection, transaction);

        Assert.Equal(T1ProbeFixture.TenantARows, visible.Count);
        Assert.All(visible, tenant => Assert.Equal(probe.TenantA, tenant));
    }

    public enum Ending { Commit, Rollback }

    // T1.2 on a reused connection — Test 27 (v1.10), first-template part: all five
    // context variables set with SET LOCAL, the transaction ends, optionally
    // DISCARD ALL, then a query with no context → zero rows and no error.
    // (Test 27's client_scope and memberships parts need those tables: T5 and T2/T3.)
    [Theory]
    [InlineData(Ending.Commit, false)]
    [InlineData(Ending.Commit, true)]
    [InlineData(Ending.Rollback, false)]
    [InlineData(Ending.Rollback, true)]
    public async Task T1_2_Test27_NoTenantContext_ReusedConnection_ZeroRowsNoError(Ending ending, bool discardAll)
    {
        await using var connection = await Target.OpenUnpooledAsync(Target.AppUser);

        await using (var first = await connection.BeginTransactionAsync())
        {
            await SetLocalAsync(connection, first, "app.user_id", Guid.CreateVersion7().ToString());
            await SetLocalAsync(connection, first, "app.tenant_id", probe.TenantA.ToString());
            await SetLocalAsync(connection, first, "app.membership_id", Guid.CreateVersion7().ToString());
            await SetLocalAsync(connection, first, "app.scope_all", "true");
            await SetLocalAsync(connection, first, "app.can_manage_scope", "true");
            Assert.NotEmpty(await VisibleTenantsAsync(connection, first));

            if (ending == Ending.Commit) await first.CommitAsync();
            else await first.RollbackAsync();
        }

        if (discardAll)
        {
            await using var discard = new NpgsqlCommand("DISCARD ALL", connection);
            await discard.ExecuteNonQueryAsync();
        }

        await using var second = await connection.BeginTransactionAsync();
        Assert.Empty(await VisibleTenantsAsync(connection, second));
    }

    // T1.3 [B] — a thousand concurrent iterations on one pool, alternating between
    // two tenants → zero leakage (Test 3). The pool is kept small so physical
    // connections are reused across tenants many times over.
    [Fact]
    public async Task T1_3_ThousandConcurrentIterations_OnePool_ZeroLeakage()
    {
        const int iterations = 1000;
        const int maxPoolSize = 8;

        var builder = new NpgsqlConnectionStringBuilder(Target.ConnectionString(Target.AppUser))
        {
            Pooling = true,
            MaxPoolSize = maxPoolSize,
            ApplicationName = "conformance-t1.3",
        };
        await using var pool = NpgsqlDataSource.Create(builder.ConnectionString);

        var backends = new ConcurrentDictionary<int, byte>();
        var leaks = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, iterations),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (i, ct) =>
            {
                var (tenant, expectedRows) = i % 2 == 0
                    ? (probe.TenantA, T1ProbeFixture.TenantARows)
                    : (probe.TenantB, T1ProbeFixture.TenantBRows);

                await using var connection = await pool.OpenConnectionAsync(ct);
                backends.TryAdd(connection.ProcessID, 0);
                await using var transaction = await connection.BeginTransactionAsync(ct);
                await SetLocalAsync(connection, transaction, "app.tenant_id", tenant.ToString());

                var visible = await VisibleTenantsAsync(connection, transaction);
                await transaction.CommitAsync(ct);

                if (visible.Count != expectedRows || visible.Any(t => t != tenant))
                    leaks.Add($"iteration {i}: expected {expectedRows} rows of {tenant}, saw [{string.Join(", ", visible)}]");
            });

        Assert.Empty(leaks);
        Assert.InRange(backends.Count, 1, maxPoolSize);
    }

    private static async Task SetLocalAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string variable, string value)
    {
        // set_config(…, true) is SET LOCAL with a bindable value.
        await using var command = new NpgsqlCommand("SELECT set_config(@name, @value, true)", connection, transaction);
        command.Parameters.AddWithValue("name", variable);
        command.Parameters.AddWithValue("value", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<Guid>> VisibleTenantsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        await using var command = new NpgsqlCommand("SELECT tenant_id FROM t1_probe", connection, transaction);
        var tenants = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tenants.Add(reader.GetGuid(0));
        return tenants;
    }
}

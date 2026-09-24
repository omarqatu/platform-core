using System.Collections.Concurrent;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC T1 — the [W] criteria, and the T1 build items proven through Core itself.
[Collection(T1ProbeCollection.Name)]
public class T1_TransactionLayerTests(T1ProbeFixture probe)
{
    // ---- T1.1 [W] — a command with no transaction → an explicit exception, not zero rows (Test 5).

    [Fact]
    public async Task T1_1_QueryOutsideTransaction_Throws()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);

        await Assert.ThrowsAsync<CommandOutsideTransactionException>(() => db.Probes.CountAsync());
    }

    [Fact]
    public async Task T1_1_RawSqlOutsideTransaction_Throws()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);

        await Assert.ThrowsAsync<CommandOutsideTransactionException>(
            () => db.Database.ExecuteSqlRawAsync("SELECT 1"));
    }

    [Fact]
    public async Task T1_1_SaveChangesOutsideTransaction_Throws_AndWritesNothing()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);
        const string label = "t1.1-save-outside";
        db.Probes.Add(new Probe { Id = Guid.CreateVersion7(), TenantId = probe.TenantA, Label = label });

        // EF wraps command failures raised during SaveChanges in DbUpdateException.
        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Contains(Chain(error), e => e is CommandOutsideTransactionException);
        Assert.Equal(0, await probe.CountLabelAsMigratorAsync(label));
    }

    [Fact]
    public async Task T1_1_InsideUnitOfWork_Succeeds()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);

        var count = await UnitOfWork.RunAsync(db, new SessionContext(null, probe.TenantA),
            (context, ct) => context.Probes.CountAsync(ct));

        Assert.Equal(T1ProbeFixture.TenantARows, count);
    }

    // ---- T1.4 [W] — a critical update to an invisible row → a rows-affected error (Test 14).

    [Fact]
    public async Task T1_4_CriticalUpdateOfInvisibleRow_ThrowsRowsAffected()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);
        var tenantBRow = probe.TenantBRowIds[0];
        var before = await probe.CounterAsMigratorAsync(tenantBRow);

        var error = await Assert.ThrowsAsync<CriticalWriteException>(() =>
            UnitOfWork.RunAsync(db, new SessionContext(null, probe.TenantA), (context, _) =>
                CriticalWrite.ExpectRowsAsync(
                    context.Probes.Where(p => p.Id == tenantBRow)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Counter, 999)),
                    expected: 1, "t1.4 update of another tenant's row")));

        Assert.Equal(0, error.Affected);
        Assert.Equal(before, await probe.CounterAsMigratorAsync(tenantBRow));
    }

    [Fact]
    public async Task T1_4_TrackedUpdateOfInvisibleRow_ThrowsConcurrency()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);
        var tenantBRow = probe.TenantBRowIds[1];
        var before = await probe.CounterAsMigratorAsync(tenantBRow);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            UnitOfWork.RunAsync(db, new SessionContext(null, probe.TenantA), (context, ct) =>
            {
                var stub = new Probe { Id = tenantBRow, TenantId = probe.TenantB, Label = "b-2" };
                context.Probes.Attach(stub);
                stub.Counter = 999;
                return context.SaveChangesAsync(ct);
            }));

        Assert.Equal(before, await probe.CounterAsMigratorAsync(tenantBRow));
    }

    [Fact]
    public async Task T1_4_CriticalUpdateOfVisibleRow_Succeeds()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        await using var db = ProbeDbContext.Create(dataSource);

        var affected = await UnitOfWork.RunAsync(db, new SessionContext(null, probe.TenantA), (context, _) =>
            CriticalWrite.ExpectRowsAsync(
                context.Probes.Where(p => p.TenantId == probe.TenantA && p.Label == "a-1")
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.Counter, p => p.Counter + 1)),
                expected: 1, "t1.4 update of own row"));

        Assert.Equal(1, affected);
    }

    // ---- T1.5 [W] — a batch update of twenty rows via EF → no phantom exceptions.

    [Fact]
    public async Task T1_5_BatchUpdateOfTwentyRows_Succeeds_InBatches_WithUntouchedCommandText()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = ProbeDbContext.Create(dataSource, recorder);
        var session = new SessionContext(null, probe.TenantA);
        var saveCommands = new List<string>();

        var (ids, before) = await UnitOfWork.RunAsync(db, session, async (context, ct) =>
        {
            var rows = await context.Probes.OrderBy(p => p.Label).Take(20).ToListAsync(ct);
            var counters = rows.ToDictionary(r => r.Id, r => r.Counter);
            foreach (var row in rows)
                row.Counter++;

            recorder.Clear();
            var saved = await context.SaveChangesAsync(ct);
            saveCommands.AddRange(recorder.Commands);

            Assert.Equal(20, saved);
            return (rows.Select(r => r.Id).ToList(), counters);
        });

        // EF sent the twenty updates in fewer commands than rows: the batching path ran.
        Assert.InRange(saveCommands.Count, 1, 19);
        // The interceptor inspects commands; nothing appended SET to their text (3.5/3).
        Assert.DoesNotContain(saveCommands, text => text.Contains("SET LOCAL", StringComparison.OrdinalIgnoreCase)
                                                  || text.Contains("set_config", StringComparison.OrdinalIgnoreCase));

        foreach (var id in ids)
            Assert.Equal(before[id] + 1, await probe.CounterAsMigratorAsync(id));
    }

    // ---- T1 build — variables: independent SET LOCAL statements, app.user_id then app.tenant_id.

    [Fact]
    public async Task UnitOfWork_SetsUserThenTenant_AsSeparateStatements_BeforeAnyAccess()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = ProbeDbContext.Create(dataSource, recorder);
        var userId = Guid.CreateVersion7();

        var (user, tenant) = await UnitOfWork.RunAsync(db, new SessionContext(userId, probe.TenantA), async (context, ct) =>
            (await SettingAsync(context, "app.user_id", ct), await SettingAsync(context, "app.tenant_id", ct)));

        Assert.Equal(userId.ToString(), user);
        Assert.Equal(probe.TenantA.ToString(), tenant);
        var commands = recorder.Commands;
        Assert.Equal($"SET LOCAL app.user_id = '{userId}'", commands[0]);
        Assert.Equal($"SET LOCAL app.tenant_id = '{probe.TenantA}'", commands[1]);
        Assert.All(commands.Skip(2), text => Assert.DoesNotContain("SET LOCAL", text));
    }

    [Fact]
    public async Task UnitOfWork_WithoutTenant_SetsNothingForIt_AndSeesZeroRows()
    {
        await using var dataSource = probe.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = ProbeDbContext.Create(dataSource, recorder);

        var count = await UnitOfWork.RunAsync(db, SessionContext.None, (context, ct) => context.Probes.CountAsync(ct));

        Assert.Equal(0, count);
        Assert.DoesNotContain(recorder.Commands, text => text.Contains("SET LOCAL"));
    }

    // ---- T1 build — connection state is reset when it returns to the pool (3.4).

    [Fact]
    public async Task Pool_SessionStateIsReset_OnTheSamePhysicalConnection()
    {
        await using var dataSource = probe.CreateAppUserDataSource(maxPoolSize: 1);

        int firstBackend;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            firstBackend = connection.ProcessID;
            // A session-level SET (not LOCAL): exactly what must not survive the pool.
            await using var set = new NpgsqlCommand($"SET app.tenant_id = '{probe.TenantA}'", connection);
            await set.ExecuteNonQueryAsync();
        }

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            Assert.Equal(firstBackend, connection.ProcessID);
            await using var read = new NpgsqlCommand("SELECT current_setting('app.tenant_id', true)", connection);
            Assert.True(string.IsNullOrEmpty((string?)await read.ExecuteScalarAsync()));
        }
    }

    [Fact]
    public void Pool_ResetOnClose_CannotBeSwitchedOff()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(probe.AppUserConnectionString) { NoResetOnClose = true }
            .ConnectionString;

        Assert.Throws<InvalidOperationException>(() => CoreDataAccess.CreateDataSource(connectionString));
    }

    // ---- T1.3 and Test 27 again, through Core's own unit of work on one small pool:
    // alternating tenants A and B, interleaved with no-context transactions.

    [Fact]
    public async Task UnitOfWork_ThousandConcurrentIterations_OnePool_ZeroLeakage()
    {
        const int iterations = 1000;
        const int maxPoolSize = 8;
        await using var dataSource = probe.CreateAppUserDataSource(maxPoolSize);
        var leaks = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, iterations),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (i, ct) =>
            {
                var (tenant, expectedRows) = (i % 3) switch
                {
                    0 => ((Guid?)probe.TenantA, T1ProbeFixture.TenantARows),
                    1 => (probe.TenantB, T1ProbeFixture.TenantBRows),
                    _ => (null, 0),
                };

                await using var db = ProbeDbContext.Create(dataSource);
                var visible = await UnitOfWork.RunAsync(db, new SessionContext(Guid.CreateVersion7(), tenant),
                    (context, token) => context.Probes.Select(p => p.TenantId).ToListAsync(token), ct);

                if (visible.Count != expectedRows || visible.Any(t => t != tenant))
                    leaks.Add($"iteration {i}: expected {expectedRows} rows of {tenant?.ToString() ?? "no tenant"}, saw {visible.Count}");
            });

        Assert.Empty(leaks);
    }

    private static async Task<string?> SettingAsync(ProbeDbContext db, string name, CancellationToken ct) =>
        await db.Database
            .SqlQueryRaw<string>("SELECT current_setting({0}, true) AS \"Value\"", name)
            .SingleAsync(ct);

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}

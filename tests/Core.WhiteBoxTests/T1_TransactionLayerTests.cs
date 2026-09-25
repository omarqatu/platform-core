using System.Collections.Concurrent;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC T1 — the [W] criteria, and the T1 build items proven through Core itself — on the real
// schema since T2: roles of the fixture's tenants W1 and W2, through Core's EF model.
[Collection(WhiteBoxCollection.Name)]
public class T1_TransactionLayerTests(WhiteBoxFixture fixture)
{
    // ---- T1.1 [W] — a command with no transaction → an explicit exception, not zero rows (Test 5).

    [Fact]
    public async Task T1_1_QueryOutsideTransaction_Throws()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        await Assert.ThrowsAsync<CommandOutsideTransactionException>(() => db.Roles.CountAsync());
    }

    [Fact]
    public async Task T1_1_RawSqlOutsideTransaction_Throws()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        await Assert.ThrowsAsync<CommandOutsideTransactionException>(() => db.Database.ExecuteSqlRawAsync("SELECT 1"));
    }

    [Fact]
    public async Task T1_1_SaveChangesOutsideTransaction_Throws_AndWritesNothing()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        const string code = "t1.1-save-outside";
        db.Roles.Add(new Role { Id = Guid.CreateVersion7(), TenantId = fixture.W1, Code = code, NameAr = "x", NameEn = "x", IsActive = true });

        // EF wraps command failures raised during SaveChanges in DbUpdateException.
        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync());
        Assert.Contains(Chain(error), e => e is CommandOutsideTransactionException);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE code = @c", ("c", code)));
    }

    [Fact]
    public async Task T1_1_InsideUnitOfWork_Succeeds()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        var count = await UnitOfWork.RunAsync(db, new SessionContext(null, fixture.W1),
            (context, ct) => context.Roles.CountAsync(ct));

        Assert.Equal(WhiteBoxFixture.W1CustomRoles + 1, count);
    }

    // ---- T1.4 [W] — a critical update to an invisible row → a rows-affected error (Test 14).

    [Fact]
    public async Task T1_4_CriticalUpdateOfInvisibleRow_ThrowsRowsAffected()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var w2Role = fixture.W2RoleIds[0];
        var before = await fixture.NameAsMigratorAsync(w2Role);

        var error = await Assert.ThrowsAsync<CriticalWriteException>(() =>
            UnitOfWork.RunAsync(db, new SessionContext(null, fixture.W1), (context, _) =>
                CriticalWrite.ExpectRowsAsync(
                    context.Roles.Where(r => r.Id == w2Role).ExecuteUpdateAsync(s => s.SetProperty(r => r.NameEn, "Hacked")),
                    expected: 1, "t1.4 update of another tenant's role")));

        Assert.Equal(0, error.Affected);
        Assert.Equal(before, await fixture.NameAsMigratorAsync(w2Role));
    }

    [Fact]
    public async Task T1_4_TrackedUpdateOfInvisibleRow_ThrowsConcurrency()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var w2Role = fixture.W2RoleIds[1];
        var before = await fixture.NameAsMigratorAsync(w2Role);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            UnitOfWork.RunAsync(db, new SessionContext(null, fixture.W1), (context, ct) =>
            {
                var stub = new Role { Id = w2Role, TenantId = fixture.W2, Code = "custom-02", NameAr = "دور", NameEn = before, IsActive = true };
                context.Roles.Attach(stub);
                stub.NameEn = "Hacked";
                return context.SaveChangesAsync(ct);
            }));

        Assert.Equal(before, await fixture.NameAsMigratorAsync(w2Role));
    }

    [Fact]
    public async Task T1_4_CriticalUpdateOfVisibleRow_Succeeds()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        var affected = await UnitOfWork.RunAsync(db, new SessionContext(null, fixture.W1), (context, _) =>
            CriticalWrite.ExpectRowsAsync(
                context.Roles.Where(r => r.TenantId == fixture.W1 && r.Code == "custom-25")
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.NameEn, "Renamed")),
                expected: 1, "t1.4 update of own role"));

        Assert.Equal(1, affected);
    }

    // ---- Test 23-b [W] (PLATFORM_CORE v1.14 §3.7): editing a system role through the layer above →
    // an explicit error from the rows-affected guard; the database below stays silent (Test 23-a).

    [Fact]
    public async Task Test23b_EditingASystemRole_IsLoudAbove()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);

        var error = await Assert.ThrowsAsync<CriticalWriteException>(() =>
            UnitOfWork.RunAsync(db, new SessionContext(null, fixture.W1), (context, _) =>
                CriticalWrite.ExpectRowsAsync(
                    context.Roles.Where(r => r.Id == fixture.W1SystemRole).ExecuteUpdateAsync(s => s.SetProperty(r => r.NameEn, "Hacked")),
                    expected: 1, "edit a system role")));

        Assert.Equal(0, error.Affected);
        Assert.Equal("Owner", await fixture.NameAsMigratorAsync(fixture.W1SystemRole));
    }

    // ---- T1.5 [W] — a batch update of twenty rows via EF → no phantom exceptions.

    [Fact]
    public async Task T1_5_BatchUpdateOfTwentyRows_Succeeds_InBatches_WithUntouchedCommandText()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);
        var saveCommands = new List<string>();
        var label = "batch-" + Guid.CreateVersion7().ToString("N")[..8];

        var ids = await UnitOfWork.RunAsync(db, new SessionContext(null, fixture.W1), async (context, ct) =>
        {
            var rows = await context.Roles.Where(r => !r.IsSystem).OrderBy(r => r.Code).Take(20).ToListAsync(ct);
            foreach (var row in rows)
                row.NameEn = label;

            recorder.Clear();
            var saved = await context.SaveChangesAsync(ct);
            saveCommands.AddRange(recorder.Commands);

            Assert.Equal(20, saved);
            return rows.Select(r => r.Id).ToList();
        });

        // EF sent the twenty updates in fewer commands than rows: the batching path ran.
        Assert.InRange(saveCommands.Count, 1, 19);
        // The interceptor inspects commands; nothing appended SET to their text (3.5/3).
        Assert.DoesNotContain(saveCommands, text => text.Contains("SET LOCAL", StringComparison.OrdinalIgnoreCase)
                                                  || text.Contains("set_config", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(20, await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE name_en = @l AND id = ANY (@ids)",
            ("l", label), ("ids", ids.ToArray())));
    }

    // ---- T1 build — variables: independent SET LOCAL statements, app.user_id then app.tenant_id.
    // (T3, decided by the project owner) then the second-axis variables in the order of 3.5/6 —
    // exactly six SET LOCAL (v1.16: app.can_manage_members last), in that order, for a real member of W1.

    [Fact]
    public async Task UnitOfWork_SetsUserThenTenant_AsSeparateStatements_BeforeAnyAccess()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);
        var userId = fixture.W1User;

        var (user, tenant) = await UnitOfWork.RunAsync(db, new SessionContext(userId, fixture.W1), async (context, ct) =>
            (await SettingAsync(context, "app.user_id", ct), await SettingAsync(context, "app.tenant_id", ct)));

        Assert.Equal(userId.ToString(), user);
        Assert.Equal(fixture.W1.ToString(), tenant);
        var setLocals = recorder.Commands.Where(text => text.Contains("SET LOCAL")).ToList();
        Assert.Equal(6, setLocals.Count);
        Assert.Equal($"SET LOCAL app.user_id = '{userId}'", setLocals[0]);
        Assert.Equal($"SET LOCAL app.tenant_id = '{fixture.W1}'", setLocals[1]);
        Assert.Equal($"SET LOCAL app.membership_id = '{fixture.W1Membership}'", setLocals[2]);
        Assert.Equal("SET LOCAL app.scope_all = 'true'", setLocals[3]);
        Assert.Equal("SET LOCAL app.can_manage_scope = 'false'", setLocals[4]);
        Assert.Equal("SET LOCAL app.can_manage_members = 'false'", setLocals[5]);
        // user and tenant first; all six before the work's first access.
        var commands = recorder.Commands.ToList();
        Assert.Equal(commands[0], setLocals[0]);
        Assert.Equal(commands[1], setLocals[1]);
        Assert.True(commands.IndexOf(setLocals[5]) < commands.FindIndex(text => text.Contains("current_setting")));
    }

    [Fact]
    public async Task UnitOfWork_WithoutTenant_SetsNothingForIt_AndSeesZeroRows()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);

        var count = await UnitOfWork.RunAsync(db, SessionContext.None, (context, ct) => context.Roles.CountAsync(ct));

        Assert.Equal(0, count);
        Assert.DoesNotContain(recorder.Commands, text => text.Contains("SET LOCAL"));
    }

    // ---- T1 build — connection state is reset when it returns to the pool (3.4).

    [Fact]
    public async Task Pool_SessionStateIsReset_OnTheSamePhysicalConnection()
    {
        await using var dataSource = fixture.CreateAppUserDataSource(maxPoolSize: 1);

        int firstBackend;
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            firstBackend = connection.ProcessID;
            // A session-level SET (not LOCAL): exactly what must not survive the pool.
            await using var set = new NpgsqlCommand($"SET app.tenant_id = '{fixture.W1}'", connection);
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
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.AppUserConnectionString) { NoResetOnClose = true }
            .ConnectionString;

        Assert.Throws<InvalidOperationException>(() => CoreDataAccess.CreateDataSource(connectionString));
    }

    // ---- T1.3 and Test 27 again, through Core's own unit of work on one small pool:
    // alternating W1 and W2, interleaved with no-context transactions.

    [Fact]
    public async Task UnitOfWork_ThousandConcurrentIterations_OnePool_ZeroLeakage()
    {
        const int iterations = 1000;
        const int maxPoolSize = 8;
        await using var dataSource = fixture.CreateAppUserDataSource(maxPoolSize);
        var leaks = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, iterations),
            new ParallelOptions { MaxDegreeOfParallelism = 32 },
            async (i, ct) =>
            {
                // (T3) Each tenant's real member: the unit of work resolves the second axis for them.
                var (user, tenant, expectedRows) = (i % 3) switch
                {
                    0 => (fixture.W1User, (Guid?)fixture.W1, WhiteBoxFixture.W1CustomRoles + 1),
                    1 => (fixture.W2User, fixture.W2, WhiteBoxFixture.W2CustomRoles),
                    _ => (Guid.CreateVersion7(), null, 0),
                };

                await using var db = WhiteBoxFixture.Context(dataSource);
                var visible = await UnitOfWork.RunAsync(db, new SessionContext(user, tenant),
                    (context, token) => context.Roles.Select(r => r.TenantId).ToListAsync(token), ct);

                if (visible.Count != expectedRows || visible.Any(t => t != tenant))
                    leaks.Add($"iteration {i}: expected {expectedRows} rows of {tenant?.ToString() ?? "no tenant"}, saw {visible.Count}");
            });

        Assert.Empty(leaks);
    }

    private static async Task<string?> SettingAsync(Core.CoreDbContext db, string name, CancellationToken ct) =>
        await db.Database
            .SqlQueryRaw<string>("SELECT current_setting({0}, true) AS \"Value\"", name)
            .SingleAsync(ct);

    private static IEnumerable<Exception> Chain(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
            yield return e;
    }
}

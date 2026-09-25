using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Core.WhiteBoxTests;

// PROOF_SPEC v1.3 T4.15 [W] — automatic auditing (PLATFORM_CORE v1.16 §7): every tracked write is audited in its
// own transaction, by a second save from SavedChanges; the self-service identity writes are exempt (login succeeds
// with no error and no entry); user_password_credentials is never audited; password_hash and token_hash are kept
// as keys valued "[masked]". Every transaction that writes seed data is rolled back.
[Collection(WhiteBoxCollection.Name)]
public class T4_AutomaticAuditTests(WhiteBoxFixture fixture)
{
    private sealed class Rollback : Exception;

    public sealed record Entry(string Action, string EntityType, Guid? EntityId, Guid TenantId, Guid? ActorId, string ActorType,
        string? OldValue, string? NewValue);

    // Insert, update, delete of one row, each its own SaveChanges: three entries, read back inside the same
    // transaction (before any commit), and not one entry for the audit saves themselves (the recursion flag).
    [Fact]
    public async Task EveryWrite_IsAudited_InItsOwnTransaction()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var roleId = Guid.CreateVersion7();

        var (entries, auditOfAudit) = await UnitOfWork.RunAsync(db, new SessionContext(fixture.W1User, fixture.W1), async (c, ct) =>
        {
            var role = new Role { Id = roleId, TenantId = fixture.W1, Code = "t4.15-" + roleId.ToString("N")[..8], NameAr = "x", NameEn = "Before", IsActive = true };
            c.Roles.Add(role);
            await c.SaveChangesAsync(ct);
            role.NameEn = "After";
            await c.SaveChangesAsync(ct);
            c.Roles.Remove(role);
            await c.SaveChangesAsync(ct);

            var found = await c.AuditLog.Where(a => a.EntityId == roleId).OrderBy(a => a.Id)
                .Select(a => new Entry(a.Action, a.EntityType, a.EntityId, a.TenantId, a.ActorId, a.ActorType, a.OldValue, a.NewValue))
                .ToListAsync(ct);
            return (found, await c.AuditLog.CountAsync(a => a.TenantId == fixture.W1 && a.EntityType == "audit_log", ct));
        });

        Assert.Equal(["insert", "update", "delete"], entries.Select(e => e.Action));
        Assert.All(entries, e =>
        {
            Assert.Equal("roles", e.EntityType);
            Assert.Equal(fixture.W1, e.TenantId);
            Assert.Equal(fixture.W1User, e.ActorId);
            Assert.Equal("user", e.ActorType);
        });
        Assert.Null(entries[0].OldValue);
        Assert.Equal("Before", Json(entries[0].NewValue)["name_en"].GetString());
        // An update records the columns written, beside the key — old and new.
        Assert.Equal(["id", "name_en"], Json(entries[1].OldValue).Keys.Order());
        Assert.Equal("Before", Json(entries[1].OldValue)["name_en"].GetString());
        Assert.Equal("After", Json(entries[1].NewValue)["name_en"].GetString());
        Assert.Equal("After", Json(entries[2].OldValue)["name_en"].GetString());
        Assert.Null(entries[2].NewValue);
        Assert.Equal(0, auditOfAudit);
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE id = @r", ("r", roleId)));
        Assert.Equal(3, await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE entity_id = @r", ("r", roleId)));
    }

    // Same transaction: the write and its entries roll back together.
    [Fact]
    public async Task AWriteThatRollsBack_LeavesNoEntry()
    {
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var roleId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<Rollback>(() => UnitOfWork.RunAsync(db, new SessionContext(fixture.W1User, fixture.W1), async (c, ct) =>
        {
            c.Roles.Add(new Role { Id = roleId, TenantId = fixture.W1, Code = "t4.15-rb-" + roleId.ToString("N")[..8], NameAr = "x", NameEn = "x", IsActive = true });
            await c.SaveChangesAsync(ct);
            Assert.Equal(1, await c.AuditLog.CountAsync(a => a.EntityId == roleId, ct));
            throw new Rollback();
        }));

        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE id = @r", ("r", roleId)));
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE entity_id = @r", ("r", roleId)));
    }

    // A write with no tenant to audit it under is refused before it reaches the database — never an unaudited write.
    // The self-service exemption covers the actor's own row only: another user's row is not self-service.
    [Fact]
    public async Task AWriteWithNoAuditTenant_IsLoud_AndSelfServiceMeansTheActorsOwnRow()
    {
        var khaled = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'khaled'");
        await using var dataSource = fixture.CreateAppUserDataSource();
        var recorder = new CommandRecorder();
        await using var db = WhiteBoxFixture.Context(dataSource, recorder);

        var error = await Assert.ThrowsAsync<AuditContextMissingException>(() =>
            UnitOfWork.RunAsync(db, new SessionContext(fixture.W1User, null), async (c, ct) =>
            {
                var stub = new User { Id = khaled };
                c.Users.Attach(stub);
                stub.LastLoginAt = DateTime.UtcNow;
                recorder.Clear();
                await c.SaveChangesAsync(ct);
            }));

        Assert.Equal("users", error.EntityType);
        Assert.Empty(recorder.Commands);
    }

    // The exemption list, through Api's real login path: last_login_at is written (a tracked self-service update),
    // the login succeeds with no error, and no entry is written for it.
    [Fact]
    public Task SelfServiceLogin_Succeeds_WithNoEntry() => InProcessApi.WithoutMigratorVariableAsync(async () =>
    {
        var khaled = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'khaled'");
        var entriesBefore = await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE entity_id = @u", ("u", khaled));
        var before = await LastLoginAsync(khaled);

        await using var api = InProcessApi.Create();
        using var client = api.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var login = await client.PostAsJsonAsync("/auth/login", new { username = "khaled", password = "khaled-seed-password" });

        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var after = await LastLoginAsync(khaled);
        Assert.NotNull(after);
        Assert.True(before is null || after > before);
        Assert.Equal(entriesBefore, await fixture.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE entity_id = @u", ("u", khaled)));
    });

    // user_password_credentials is never audited: a person, a user and their credential → entries for the person
    // and the user, none for the credential. As migrator, in a transaction rolled back.
    [Fact]
    public async Task PasswordCredentials_AreNeverAudited()
    {
        await using var dataSource = fixture.CreateMigratorDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.Audit = new AuditContext(fixture.W1, null, "test", null);
        var person = new Person { Id = Guid.CreateVersion7(), FullName = "T4.15", Email = $"t4.15-{Guid.CreateVersion7():N}@test", CreatedAt = DateTime.UtcNow };
        var user = new User { Id = Guid.CreateVersion7(), PersonId = person.Id, UserType = "employee", Username = $"t4.15-{Guid.CreateVersion7():N}", Status = "active" };
        db.AddRange(person, user);
        await db.SaveChangesAsync();
        // The model maps no relationships, so EF orders no inserts: the credential goes in a save of its own.
        db.Add(new UserPasswordCredential { UserId = user.Id, PasswordHash = "secret-hash", UpdatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var types = await db.AuditLog.Where(a => a.TenantId == fixture.W1 && (a.EntityId == person.Id || a.EntityId == user.Id || a.EntityId == null))
            .Select(a => a.EntityType).OrderBy(t => t).ToListAsync();
        var values = await db.AuditLog.Where(a => a.TenantId == fixture.W1).Select(a => a.NewValue).ToListAsync();
        await transaction.RollbackAsync();

        Assert.Equal(["persons", "users"], types);
        Assert.DoesNotContain(values, v => v != null && v.Contains("secret-hash"));
    }

    // The masking list: token_hash stays in new_value, valued "[masked]" — the column was written, its value not
    // revealed. Sara (Al-Amin admin, core.members.manage) creates an invitation; the transaction rolls back.
    // password_hash is on the list too, as the second guard of a table that is never audited (above).
    [Fact]
    public async Task TokenHash_IsKept_AndMasked()
    {
        var sara = await ScalarGuidAsync("SELECT id FROM users WHERE username = 'sara'");
        var alAmin = await ScalarGuidAsync("SELECT id FROM tenants WHERE name = 'Al-Amin'");
        var viewer = await ScalarGuidAsync("SELECT r.id FROM roles r JOIN tenants t ON t.id = r.tenant_id WHERE t.name = 'Al-Amin' AND r.code = 'viewer'");
        await using var dataSource = fixture.CreateAppUserDataSource();
        await using var db = WhiteBoxFixture.Context(dataSource);
        var invitation = Guid.CreateVersion7();
        string? newValue = null;

        await Assert.ThrowsAsync<Rollback>(() => UnitOfWork.RunAsync(db, new SessionContext(sara, alAmin), async (c, ct) =>
        {
            c.Invitations.Add(new Invitation
            {
                Id = invitation, TenantId = alAmin, Email = "t4.15@test", RoleId = viewer, TokenHash = "the-token-hash-value",
                Status = "pending", InvitedBy = sara, ExpiresAt = DateTime.UtcNow.AddDays(7), CreatedAt = DateTime.UtcNow,
                IntendedScopeMode = "assigned",
            });
            await c.SaveChangesAsync(ct);
            newValue = await c.AuditLog.Where(a => a.EntityId == invitation).Select(a => a.NewValue).SingleAsync(ct);
            throw new Rollback();
        }));

        Assert.Equal(AutomaticAuditInterceptor.Masked, Json(newValue)["token_hash"].GetString());
        Assert.DoesNotContain("the-token-hash-value", newValue);
        Assert.Equal("t4.15@test", Json(newValue)["email"].GetString());
        Assert.Equal(["password_hash", "token_hash"], AutomaticAuditInterceptor.MaskedColumns.Order());
        Assert.Equal(0, await fixture.CountAsMigratorAsync("SELECT count(*) FROM invitations WHERE id = @i", ("i", invitation)));
    }

    private static Dictionary<string, JsonElement> Json(string? value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(value ?? throw new Xunit.Sdk.XunitException("No JSON value."))!;

    private async Task<DateTime?> LastLoginAsync(Guid user)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT last_login_at FROM users WHERE id = @u", connection);
        command.Parameters.AddWithValue("u", user);
        return await command.ExecuteScalarAsync() is DateTime value ? value : null;
    }

    private async Task<Guid> ScalarGuidAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No row: {sql}"));
    }
}

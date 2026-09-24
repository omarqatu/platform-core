using Npgsql;

namespace Conformance;

// PROOF_SPEC T0 — the five roles. Document tests 9 and 22.
public class T0_RolesTests
{
    public static TheoryData<string, string> ImpersonationAttempts()
    {
        var data = new TheoryData<string, string>();
        foreach (var from in Target.AllRoles.Except(Target.PrivilegedRoles))
            foreach (var to in Target.PrivilegedRoles)
                data.Add(from, to);
        data.Add(Target.Provisioner, Target.Migrator);
        return data;
    }

    // T0.1 [B] — SET ROLE migrator / provisioner from an application connection fails (Test 22).
    [Theory]
    [MemberData(nameof(ImpersonationAttempts))]
    public async Task T0_1_SetRoleToPrivilegedRoleFails(string from, string to)
    {
        await using var connection = await Target.OpenAsync(from);
        await using var command = new NpgsqlCommand(
            $"SET ROLE {new NpgsqlCommandBuilder().QuoteIdentifier(Target.RoleName(to))}", connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    // T0.2 [B] — no application role is a member of migrator or provisioner, directly
    // (pg_auth_members) or transitively (Tests 9, 22).
    [Fact]
    public async Task T0_2_NoApplicationRoleIsMemberOfPrivilegedRole()
    {
        await using var connection = await Target.OpenAsync(Target.AppUser);
        await using var command = new NpgsqlCommand(
            """
            SELECT member.rolname || ' -> ' || target.rolname
            FROM pg_roles member
            CROSS JOIN pg_roles target
            WHERE member.rolname = ANY(@members)
              AND target.rolname = ANY(@targets)
              AND member.oid <> target.oid
              AND (pg_has_role(member.oid, target.oid, 'MEMBER')
                   OR EXISTS (SELECT 1 FROM pg_auth_members am
                              WHERE am.member = member.oid AND am.roleid = target.oid))
            """, connection);
        command.Parameters.AddWithValue("members",
            Target.AllRoles.Where(r => r != Target.Migrator).Select(Target.RoleName).ToArray());
        command.Parameters.AddWithValue("targets", Target.PrivilegedRoles.Select(Target.RoleName).ToArray());

        Assert.Empty(await Target.QueryStringsAsync(command));
    }

    // PROOF_SPEC T0 build + rule 1/4 — no role is a superuser; BYPASSRLS belongs to migrator alone.
    [Fact]
    public async Task NoSuperuser_And_BypassRlsOnlyForMigrator()
    {
        await using var connection = await Target.OpenAsync(Target.AppUser);
        await using var command = new NpgsqlCommand(
            "SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = ANY(@roles)", connection);
        command.Parameters.AddWithValue("roles", Target.AllRoles.Select(Target.RoleName).ToArray());

        var roles = new Dictionary<string, (bool Super, bool BypassRls)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                roles[reader.GetString(0)] = (reader.GetBoolean(1), reader.GetBoolean(2));
        }

        Assert.Equal(Target.AllRoles.Length, roles.Count);
        foreach (var role in Target.AllRoles)
        {
            var attributes = roles[Target.RoleName(role)];
            Assert.False(attributes.Super, $"{role} must not be a superuser.");
            Assert.True(attributes.BypassRls == (role == Target.Migrator),
                $"{role}: BYPASSRLS = {attributes.BypassRls}; only migrator may hold it.");
        }
    }

    // PROOF_SPEC T0 build — no application role is an owner: not of the database,
    // a schema, a relation, or a function.
    [Fact]
    public async Task NoApplicationRoleOwnsAnything()
    {
        await using var connection = await Target.OpenAsync(Target.AppUser);
        await using var command = new NpgsqlCommand(
            """
            SELECT 'database ' || datname FROM pg_database
             WHERE datname = current_database() AND pg_get_userbyid(datdba) = ANY(@roles)
            UNION ALL
            SELECT 'schema ' || nspname FROM pg_namespace
             WHERE pg_get_userbyid(nspowner) = ANY(@roles)
            UNION ALL
            SELECT 'relation ' || relname FROM pg_class
             WHERE pg_get_userbyid(relowner) = ANY(@roles)
            UNION ALL
            SELECT 'function ' || proname FROM pg_proc
             WHERE pg_get_userbyid(proowner) = ANY(@roles)
            """, connection);
        command.Parameters.AddWithValue("roles",
            Target.AllRoles.Where(r => r != Target.Migrator).Select(Target.RoleName).ToArray());

        Assert.Empty(await Target.QueryStringsAsync(command));
    }
}

using System.Collections.Concurrent;
using Npgsql;

namespace Conformance;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SchemaPlantCollection
{
    public const string Name = "schema plant (runs alone)";
}

// PLATFORM_CORE v1.14 §3.7 Test 8: nested policy chains execute without recursion under load — and a
// guard fails loudly if a future policy closes a cycle (§4.6). The guard commits a planted policy as
// migrator, so this class runs alone and always removes it.
[Collection(SchemaPlantCollection.Name)]
public class T2_Test8
{
    [Fact]
    public async Task Test08_NestedChains_UnderLoad_NoRecursion()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var sara = await Seed.UserAsync("sara");
        var errors = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(Enumerable.Range(0, 400), new ParallelOptions { MaxDegreeOfParallelism = 16 }, async (i, _) =>
        {
            try
            {
                await using var session = await Session.OpenAsync(Target.AppUser, user: sara, tenant: alAmin);
                // persons → users → memberships; users → memberships; tenants → memberships; membership_auth → memberships.
                var persons = await session.CountAsync("SELECT count(*) FROM persons");
                var tenants = await session.CountAsync("SELECT count(*) FROM tenants");
                var auth = await session.CountAsync("SELECT count(*) FROM membership_auth");
                if (persons != 5 || tenants != 1 || auth != 1)
                    errors.Add($"iteration {i}: persons {persons}, tenants {tenants}, membership_auth {auth}");
            }
            catch (Exception e)
            {
                errors.Add($"iteration {i}: {e.Message}");
            }
        });

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Test08_Guard_APolicyClosingACycle_FailsLoudly()
    {
        const string plant =
            "CREATE POLICY test8_cycle ON memberships FOR SELECT TO app_user " +
            "USING (EXISTS (SELECT 1 FROM persons p JOIN users u ON u.person_id = p.id WHERE u.id = memberships.user_id))";
        await ExecuteAsMigratorAsync(plant);
        try
        {
            await using var session = await Session.OpenAsync(Target.AppUser,
                user: await Seed.UserAsync("sara"), tenant: await Seed.TenantAsync(Seed.AlAmin));

            // memberships → persons → users → memberships: a closed cycle.
            Assert.Equal("42P17", await session.SqlStateOfAsync("SELECT count(*) FROM persons"));
        }
        finally
        {
            await ExecuteAsMigratorAsync("DROP POLICY IF EXISTS test8_cycle ON memberships");
        }
    }

    private static async Task ExecuteAsMigratorAsync(string sql)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

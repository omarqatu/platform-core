using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Conformance;

// PROOF_SPEC v1.3 T5 — the first scoped module (PLATFORM_CORE v1.16 §3.1, §3.5, §4.8; Tests 16, 17, 19, 20, 21, 24,
// 25, 27). Reads run against the seed contract (§7): Al-Amin's clients A–D with 5 subscriptions each, Maan's X, Y
// with 3 each; Khaled assigned A and B, Rami assigned A (a scope manager), Sara and Layla 'all', Omar 'assigned' in
// Maan with no assignment. Writes to the seed tenants happen only in sessions that roll back; what the API commits
// happens in a tenant of the test's own (World).
public class T5_ScopedModuleTests
{
    private static readonly string[] Endpoints = ["/subscriptions", "/subscriptions/clients", "/audit-log"];

    // ---- T5.1 — Test 16 in full.

    // a. Leakage: an assigned member, assigned A and B, with no filter → rows of A and B only, through the API and
    // beneath it; a write for a client outside the assignment fails under WITH CHECK.
    [Fact]
    public async Task T5_1_Test16a_Leakage_AssignedSeesOnlyTheirClients()
    {
        var a = await Seed.ClientAsync(Seed.AlAmin, "A");
        var b = await Seed.ClientAsync(Seed.AlAmin, "B");
        var c = await Seed.ClientAsync(Seed.AlAmin, "C");
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var subscriptions = await ListAsync(khaled, "/subscriptions");
        var clients = await ListAsync(khaled, "/subscriptions/clients");

        Assert.Equal(10, subscriptions.GetProperty("visible_count").GetInt64());
        Assert.All(Items(subscriptions), s => Assert.Contains(s.GetProperty("client_id").GetGuid(), new[] { a, b }));
        Assert.Equal(new[] { a, b }.Order(), Items(clients).Select(x => x.GetProperty("id").GetGuid()).Order());

        var create = await khaled.Client.PostAsJsonAsync("/subscriptions", new { client_id = c, service_name = "leak", ends_on = "2027-06-01" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);

        await using var session = await SessionAsync("khaled", Seed.AlAmin);
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM subscriptions WHERE scope_ref_id NOT IN (@a, @b)", ("a", a), ("b", b)));
        Assert.Equal("42501", await session.SqlStateOfAsync(InsertSubscription, ("c", c)));
    }

    // b. Blinding: an 'all' member sees A and B together, and everything — nothing hidden by the restrictive policy.
    // And the decisive branch (1.8): an assigned member who is actually assigned sees A's rows, not zero.
    [Fact]
    public async Task T5_1_Test16b_Blinding_AllSeesEverything_AndTheAssignedSeesTheirRows()
    {
        var a = await Seed.ClientAsync(Seed.AlAmin, "A");
        var tenantTotal = await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM subscriptions s JOIN tenants t ON t.id = s.tenant_id WHERE t.name = @n", ("n", Seed.AlAmin));
        var ofA = await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE scope_ref_id = @a", ("a", a));
        using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin);
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        Assert.Equal(tenantTotal, (await ListAsync(sara, "/subscriptions?limit=200")).GetProperty("visible_count").GetInt64());
        Assert.Equal(4, (await ListAsync(sara, "/subscriptions/clients")).GetProperty("visible_count").GetInt64());
        var khaledsOfA = Items(await ListAsync(khaled, "/subscriptions?limit=200")).Count(s => s.GetProperty("client_id").GetGuid() == a);
        Assert.Equal(ofA, khaledsOfA);
        Assert.True(khaledsOfA > 0);

        await using var session = await SessionAsync("khaled", Seed.AlAmin);
        Assert.Equal(ofA, await session.CountAsync("SELECT count(*) FROM subscriptions WHERE scope_ref_id = @a", ("a", a)));
    }

    // c. Declaration: the assigned member's aggregate response carries scope_mode and visible_count, and total_count
    // as an explicit null — the field present.
    [Fact]
    public async Task T5_1_Test16c_Declaration()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var body = await ListAsync(khaled, "/subscriptions");

        Assert.Equal("assigned", body.GetProperty("scope_mode").GetString());
        Assert.Equal(10, body.GetProperty("visible_count").GetInt64());
        Assert.True(body.TryGetProperty("total_count", out var total));
        Assert.Equal(JsonValueKind.Null, total.ValueKind);
        Assert.True(body.TryGetProperty("has_more_in_scope", out _));
    }

    // d. Reading assignments: an assigned member reads their own, not another membership's, and cannot write them.
    [Fact]
    public async Task T5_1_Test16d_ReadingAssignments()
    {
        var rami = await Seed.MembershipAsync("rami", Seed.AlAmin);
        await using var session = await SessionAsync("khaled", Seed.AlAmin);

        Assert.Equal(2, await session.CountAsync("SELECT count(*) FROM scope_assignments"));
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM scope_assignments WHERE membership_id = @r", ("r", rami)));
        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active) " +
            "SELECT @id, tenant_id, membership_id, @c, 'contributor', true FROM scope_assignments LIMIT 1",
            ("id", Guid.CreateVersion7()), ("c", await Seed.ClientAsync(Seed.AlAmin, "C"))));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE scope_assignments SET active = false"));
    }

    // ---- T5.2 — Test 17: self-escalation of scope blocked by both layers.
    [Fact]
    public async Task T5_2_Test17_SelfEscalation_Blocked()
    {
        // a. Even with an admin role, one's own membership_scope row is not updatable.
        await using (var sara = await SessionAsync("sara", Seed.AlAmin))
            Assert.Equal(0, await sara.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'assigned' WHERE membership_id = @m",
                ("m", await Seed.MembershipAsync("sara", Seed.AlAmin))));
        await using (var rami = await SessionAsync("rami", Seed.AlAmin))
            Assert.Equal(0, await rami.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = @m",
                ("m", await Seed.MembershipAsync("rami", Seed.AlAmin))));
        // b. No scope column on memberships to raise through the self-departure path.
        Assert.Equal(0, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'memberships' AND column_name LIKE 'scope%'"));
        // c. The scope from a header, the query string or the payload → no effect on the module's surface.
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/subscriptions?scope_all=true&app.scope_all=true&limit=200");
        request.Headers.Add("X-Scope-All", "true");
        request.Headers.Add("app.scope_all", "true");
        var response = await khaled.Client.SendAsync(request);
        var body = await khaled.JsonAsync(response);
        Assert.Equal("assigned", body.GetProperty("scope_mode").GetString());
        Assert.Equal(10, body.GetProperty("visible_count").GetInt64());
    }

    // ---- T5.3 — Test 19: total_count null for an assigned member on every endpoint, a real number for an 'all' one.
    [Theory]
    [InlineData("khaled", Seed.AlAmin)]
    [InlineData("rami", Seed.AlAmin)]
    [InlineData("omar", Seed.Maan)]
    public async Task T5_3_Test19_Assigned_TotalCountIsNull_OnEveryEndpoint(string username, string tenant)
    {
        using var browser = await Browser.SignedInAsync(username, tenant);
        foreach (var endpoint in Endpoints)
        {
            var body = await ListAsync(browser, endpoint);
            Assert.Equal("assigned", body.GetProperty("scope_mode").GetString());
            Assert.True(body.TryGetProperty("total_count", out var total), endpoint);
            Assert.Equal(JsonValueKind.Null, total.ValueKind);
        }
    }

    [Theory]
    [InlineData("sara", Seed.AlAmin)]
    [InlineData("layla", Seed.AlAmin)]
    [InlineData("nour", Seed.Maan)]
    public async Task T5_3_Test19_All_TotalCountIsANumber_OnEveryEndpoint(string username, string tenant)
    {
        using var browser = await Browser.SignedInAsync(username, tenant);
        foreach (var endpoint in Endpoints)
        {
            var body = await ListAsync(browser, endpoint);
            Assert.Equal("all", body.GetProperty("scope_mode").GetString());
            Assert.Equal(JsonValueKind.Number, body.GetProperty("total_count").ValueKind);
            Assert.Equal(body.GetProperty("visible_count").GetInt64(), body.GetProperty("total_count").GetInt64());
        }
    }

    // ---- T5.4 — Test 24 in full.
    [Fact]
    public async Task T5_4_Test24a_ViewerWithAll_CannotManageAssignments_FromTheDatabase()
    {
        var c = await Seed.ClientAsync(Seed.AlAmin, "C");
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        await using var layla = await SessionAsync("layla", Seed.AlAmin);

        Assert.Equal("42501", await layla.SqlStateOfAsync(InsertAssignment, ("t", await Seed.TenantAsync(Seed.AlAmin)), ("m", khaled), ("c", c)));
        Assert.Equal(0, await layla.ExecuteAsync("UPDATE scope_assignments SET active = false WHERE membership_id = @m", ("m", khaled)));
        Assert.Equal(0, await layla.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = @m", ("m", khaled)));
    }

    [Fact]
    public async Task T5_4_Test24b_ScopeManagerWithAssigned_ManagesOthers_SeesOnlyTheirOwn()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var a = await Seed.ClientAsync(Seed.AlAmin, "A");
        var c = await Seed.ClientAsync(Seed.AlAmin, "C");
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        await using (var rami = await SessionAsync("rami", Seed.AlAmin))
        {
            // Manages another member's assignments…
            Assert.Equal("no error", await rami.SqlStateOfAsync(InsertAssignment, ("t", alAmin), ("m", khaled), ("c", c)));
            Assert.Equal(1, await rami.ExecuteAsync("UPDATE scope_assignments SET active = false WHERE membership_id = @m AND scope_ref_id = @c",
                ("m", khaled), ("c", c)));
            // …and by that alone sees no more than their own assigned client on the scoped tables.
            Assert.Equal(0, await rami.CountAsync("SELECT count(*) FROM subscriptions WHERE scope_ref_id <> @a", ("a", a)));
            Assert.Equal(1, await rami.CountAsync("SELECT count(*) FROM clients"));
        }

        using var browser = await Browser.SignedInAsync("rami", Seed.AlAmin);
        var clients = await ListAsync(browser, "/subscriptions/clients");
        Assert.Equal([a], Items(clients).Select(x => x.GetProperty("id").GetGuid()));
        Assert.All(Items(await ListAsync(browser, "/subscriptions?limit=200")), s => Assert.Equal(a, s.GetProperty("client_id").GetGuid()));
    }

    // ---- T5.5 — Test 25: an assigned member reads zero audit rows — those of their unassigned entities included — and
    // the surface declares the blinding; an 'all' member in the same tenant sees the full log.
    [Fact]
    public async Task T5_5_Test25_AuditLog_ZeroForAssigned_DeclaredNotEmpty()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var c = await Seed.ClientAsync(Seed.AlAmin, "C");
        var a = await Seed.ClientAsync(Seed.AlAmin, "A");
        var entries = new[] { Guid.CreateVersion7(), Guid.CreateVersion7() };
        await World.ExecuteAsMigratorAsync(
            "INSERT INTO audit_log (id, tenant_id, actor_type, action, entity_type, entity_id, old_value, new_value, created_at) VALUES " +
            "(@e1, @t, 'test', 'update', 'clients', @c, '{\"name\": \"C\"}', '{\"name\": \"C2\"}', now()), " +
            "(@e2, @t, 'test', 'update', 'clients', @a, '{\"name\": \"A\"}', '{\"name\": \"A2\"}', now())",
            ("e1", entries[0]), ("e2", entries[1]), ("t", alAmin), ("c", c), ("a", a));
        try
        {
            using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);
            var assigned = await ListAsync(khaled, "/audit-log");
            Assert.Equal("assigned", assigned.GetProperty("scope_mode").GetString());
            Assert.Equal(0, assigned.GetProperty("visible_count").GetInt64());
            Assert.Equal(JsonValueKind.Null, assigned.GetProperty("total_count").ValueKind);
            await using (var session = await SessionAsync("khaled", Seed.AlAmin))
                Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM audit_log"));

            // The 'all' member sees the log in full — both entries, content included. (Other tests commit entries in
            // Al-Amin concurrently, so the check is on this test's own rows, not on a total taken at another moment.)
            using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin);
            var all = await ListAsync(sara, "/audit-log");
            Assert.Equal("all", all.GetProperty("scope_mode").GetString());
            Assert.True(all.GetProperty("visible_count").GetInt64() >= 2);
            Assert.Equal(all.GetProperty("visible_count").GetInt64(), all.GetProperty("total_count").GetInt64());
            await using (var session = await SessionAsync("sara", Seed.AlAmin))
            {
                Assert.Equal(2, await session.CountAsync("SELECT count(*) FROM audit_log WHERE id = ANY (@e)", ("e", entries)));
                Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM audit_log WHERE id = @e AND new_value ->> 'name' = 'C2'", ("e", entries[0])));
            }
        }
        finally
        {
            await World.ExecuteAsMigratorAsync("DELETE FROM audit_log WHERE id = ANY (@e)", ("e", entries));
        }
    }

    // ---- T5.6 — an assigned member inserting a subscription for an unassigned client → fails under WITH CHECK.
    [Fact]
    public async Task T5_6_InsertForUnassignedClient_FailsUnderWithCheck()
    {
        await using var session = await SessionAsync("khaled", Seed.AlAmin);

        Assert.Equal("42501", await session.SqlStateOfAsync(InsertSubscription, ("c", await Seed.ClientAsync(Seed.AlAmin, "C"))));
        Assert.Equal("no error", await session.SqlStateOfAsync(InsertSubscription, ("c", await Seed.ClientAsync(Seed.AlAmin, "A"))));
    }

    // ---- T5.7 — Check 8: both tables carrying scope_ref_id have a restrictive client_scope and scope_ref_id NOT NULL.
    [Fact]
    public async Task T5_7_Check8_BothScopedTables_HaveClientScope_AndNotNull()
    {
        foreach (var table in new[] { "clients", "subscriptions" })
        {
            Assert.Equal(1, await Seed.CountAsMigratorAsync(
                "SELECT count(*) FROM pg_policy WHERE polrelid = @t::regclass AND polname = 'client_scope' AND NOT polpermissive AND polcmd = '*'",
                ("t", table)));
            Assert.Equal(1, await Seed.CountAsMigratorAsync(
                "SELECT count(*) FROM pg_attribute WHERE attrelid = @t::regclass AND attname = 'scope_ref_id' AND attnotnull", ("t", table)));
        }
    }

    // ---- T5.8 — Test 27, the client_scope part: on a reused connection where the second-axis variables were set, a
    // query with no context → zero rows, with no error; after COMMIT or ROLLBACK, with or without DISCARD ALL.
    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task T5_8_Test27_ClientScope_ReusedConnection_ZeroRowsNoError(bool commit, bool discardAll)
    {
        await using var connection = await Target.OpenUnpooledAsync(Target.AppUser);
        await using (var first = await connection.BeginTransactionAsync())
        {
            foreach (var (name, value) in await SixVariablesAsync("sara", Seed.AlAmin))
                await SetAsync(connection, first, name, value);
            Assert.True(await CountAsync(connection, first, "SELECT count(*) FROM subscriptions") > 0);
            if (commit) await first.CommitAsync();
            else await first.RollbackAsync();
        }
        if (discardAll)
        {
            await using var discard = new NpgsqlCommand("DISCARD ALL", connection);
            await discard.ExecuteNonQueryAsync();
        }

        await using var second = await connection.BeginTransactionAsync();
        Assert.Equal(0, await CountAsync(connection, second, "SELECT count(*) FROM subscriptions"));
        Assert.Equal(0, await CountAsync(connection, second, "SELECT count(*) FROM clients"));
    }

    // ---- T5.9 — Test 21, the scope_ref_id part: a subscription's scope_ref_id changed to another tenant's client →
    // rejected by the module's composite FK (23503) — as migrator, and as app_user, who holds UPDATE on module tables.
    [Fact]
    public async Task T5_9_Test21_ScopeRefIdToAnotherTenantsClient_RejectedByTheCompositeFk()
    {
        var a = await Seed.ClientAsync(Seed.AlAmin, "A");
        var x = await Seed.ClientAsync(Seed.Maan, "X");

        await using (var migrator = await Session.OpenAsync(Target.Migrator))
            Assert.Equal("23503", await migrator.SqlStateOfAsync(
                "UPDATE subscriptions SET scope_ref_id = @x WHERE scope_ref_id = @a", ("x", x), ("a", a)));
        await using (var sara = await SessionAsync("sara", Seed.AlAmin))
            Assert.Equal("23503", await sara.SqlStateOfAsync(
                "UPDATE subscriptions SET scope_ref_id = @x WHERE scope_ref_id = @a", ("x", x), ("a", a)));
    }

    // ---- Test 20, the visible-rows part (OPEN_ITEMS 13): a revoked assignment, and a lowered mode, during a live
    // session → the next request sees zero rows of that entity, with no re-login. In a tenant of the test's own.
    [Fact]
    public async Task Test20_RevokedAssignment_AndLoweredMode_NextRequestSeesZero()
    {
        using var world = await World.BootstrapAsync("t5-20");
        var p = await world.ClientAsync("P");
        var q = await world.ClientAsync("Q");
        foreach (var client in new[] { p, q })
            Assert.Equal(HttpStatusCode.Created, (await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions",
                new { client_id = client, service_name = "t5-20", ends_on = "2027-06-01" })).StatusCode);
        using var assigned = await world.JoinAsync("t5-20-assigned", "operator", "assigned");
        using var all = await world.JoinAsync("t5-20-all", "viewer", "all");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{p}", new { active = true, reason = "t5-20" })).Status);

        Assert.Equal([p], ClientIds(await ListAsync(assigned.Browser, "/subscriptions")));
        Assert.Equal([p, q], ClientIds(await ListAsync(all.Browser, "/subscriptions")).Order());

        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{p}", new { active = false, reason = "revoked" })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{all.MembershipId}/mode", new { scope_mode = "assigned" })).Status);

        var afterRevoke = await ListAsync(assigned.Browser, "/subscriptions");
        var afterLowering = await ListAsync(all.Browser, "/subscriptions");
        Assert.Equal(0, afterRevoke.GetProperty("visible_count").GetInt64());
        Assert.Equal(0, afterLowering.GetProperty("visible_count").GetInt64());
        Assert.Equal("assigned", afterLowering.GetProperty("scope_mode").GetString());
    }

    // ---- The creation path, through the API, and its audit entry (7): in a tenant of the test's own.
    [Fact]
    public async Task CreatingASubscription_WritesIt_AndItsAuditEntry()
    {
        using var world = await World.BootstrapAsync("t5-create");
        var p = await world.ClientAsync("P");

        var response = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions",
            new { client_id = p, service_name = "created", ends_on = "2027-06-01" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await world.Owner.Browser.JsonAsync(response)).GetProperty("id").GetGuid();
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i AND scope_ref_id = @p", ("i", id), ("p", p)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_type = 'subscriptions' AND entity_id = @i AND action = 'insert' AND actor_id = @u",
            ("t", world.TenantId), ("i", id), ("u", world.Owner.UserId)));

        // Another tenant's client: the composite FK, not a leak of whether it exists.
        var x = await Seed.ClientAsync(Seed.Maan, "X");
        var foreign = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions", new { client_id = x, service_name = "x", ends_on = "2027-06-01" });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("invalid_reference", (await world.Owner.Browser.JsonAsync(foreign)).GetProperty("error").GetString());
    }

    // ---- The explicit permission (5): a viewer lists but cannot create.
    [Fact]
    public async Task AViewer_Lists_ButCannotCreate()
    {
        using var layla = await Browser.SignedInAsync("layla", Seed.AlAmin);

        Assert.Equal(HttpStatusCode.OK, (await layla.Client.GetAsync("/subscriptions")).StatusCode);
        var create = await layla.Client.PostAsJsonAsync("/subscriptions",
            new { client_id = await Seed.ClientAsync(Seed.AlAmin, "A"), service_name = "x", ends_on = "2027-06-01" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal("not_permitted", (await layla.JsonAsync(create)).GetProperty("error").GetString());
    }

    // ---- helpers

    private const string InsertSubscription =
        "INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at) " +
        "VALUES (uuidv7(), current_setting('app.tenant_id')::uuid, @c, 'conformance', DATE '2027-06-01', now())";

    private const string InsertAssignment =
        "INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active) " +
        "VALUES (uuidv7(), @t, @m, @c, 'contributor', true)";

    private static async Task<JsonElement> ListAsync(Browser browser, string path)
    {
        var (status, body) = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    private static List<JsonElement> Items(JsonElement body) => body.GetProperty("items").EnumerateArray().ToList();

    private static List<Guid> ClientIds(JsonElement body) =>
        Items(body).Select(s => s.GetProperty("client_id").GetGuid()).Distinct().ToList();

    // A session as the member would have it: the six variables with their resolved values (3.5/6), as migrator reads them.
    private static async Task<Session> SessionAsync(string username, string tenant)
    {
        var values = (await SixVariablesAsync(username, tenant)).ToDictionary(v => v.Name, v => v.Value);
        return await Session.OpenAsync(Target.AppUser, user: Guid.Parse(values["app.user_id"]), tenant: Guid.Parse(values["app.tenant_id"]),
            membership: Guid.Parse(values["app.membership_id"]), scopeAll: values["app.scope_all"] == "true",
            canManageScope: values["app.can_manage_scope"] == "true", canManageMembers: values["app.can_manage_members"] == "true");
    }

    private static async Task<List<(string Name, string Value)>> SixVariablesAsync(string username, string tenant)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(
            """
            SELECT u.id::text, t.id::text, m.id::text, (s.scope_mode = 'all')::text,
                   EXISTS (SELECT 1 FROM membership_roles mr JOIN role_permissions rp ON rp.role_id = mr.role_id
                           JOIN permissions p ON p.id = rp.permission_id WHERE mr.membership_id = m.id AND p.code = 'core.scope.manage')::text,
                   EXISTS (SELECT 1 FROM membership_roles mr JOIN role_permissions rp ON rp.role_id = mr.role_id
                           JOIN permissions p ON p.id = rp.permission_id WHERE mr.membership_id = m.id AND p.code = 'core.members.manage')::text
            FROM users u JOIN memberships m ON m.user_id = u.id JOIN tenants t ON t.id = m.tenant_id
            JOIN membership_scope s ON s.membership_id = m.id
            WHERE u.username = @u AND t.name = @t
            """, connection);
        command.Parameters.AddWithValue("u", username);
        command.Parameters.AddWithValue("t", tenant);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return
        [
            ("app.user_id", reader.GetString(0)), ("app.tenant_id", reader.GetString(1)), ("app.membership_id", reader.GetString(2)),
            ("app.scope_all", reader.GetString(3)), ("app.can_manage_scope", reader.GetString(4)), ("app.can_manage_members", reader.GetString(5)),
        ];
    }

    private static async Task SetAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string name, string value)
    {
        await using var command = new NpgsqlCommand("SELECT set_config(@n, @v, true)", connection, transaction);
        command.Parameters.AddWithValue("n", name);
        command.Parameters.AddWithValue("v", value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}

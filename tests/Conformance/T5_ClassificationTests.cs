using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;

namespace Conformance;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ClassificationCollection
{
    // Alone: one test commits a subcategory to the global catalog for its length, and the catalog tests read it whole.
    public const string Name = "classification catalog";
}

// PLATFORM_CORE Amendment 02, stage (a) — the classification and the subscription's own fields (§2, §3.1, §3.2, §5,
// §5.1). The catalog is global: read by app_user and provisioner, written by neither — job_runner's read of §5 is
// deferred, so it keeps v1.16's surface (T7.4; OPEN_ITEMS 76). The columns are
// written under subscriptions.write and pass the same two templates as before. What the API commits happens in a
// tenant of the test's own (World); the seed tenants are only read, or written in sessions that roll back.
[Collection(ClassificationCollection.Name)]
public class T5_ClassificationTests
{
    private static readonly string[] Seven = ["domains", "hosting", "security", "software", "backup", "ai", "other"];

    // ---- §3.1: the seven, in the decision's order, with no subcategory — to any member who reads subscriptions,
    // whatever their scope: the catalog is not narrowed by the second axis.
    [Theory]
    [InlineData("omar")]     // owner, all
    [InlineData("khaled")]   // operator, assigned
    [InlineData("layla")]    // viewer, all
    public async Task TheCatalog_IsTheSeven_InOrder_ForEveryMember(string username)
    {
        using var member = await Browser.SignedInAsync(username, Seed.AlAmin);
        var (status, body) = await member.GetAsync("/subscriptions/categories");

        Assert.Equal(HttpStatusCode.OK, status);
        var categories = body.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(Seven, categories.Select(c => c.GetProperty("code").GetString()));
        Assert.All(categories, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.GetProperty("name_ar").GetString()));
            Assert.False(string.IsNullOrEmpty(c.GetProperty("name_en").GetString()));
            Assert.Equal(0, c.GetProperty("subcategories").GetArrayLength());
        });
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subcategories"));
    }

    // ---- §5: SELECT for app_user and provisioner; no write for either. With no context set at all: the catalog is
    // global, so no tenant is needed to read it (USING (true)).
    [Theory]
    [InlineData(Target.AppUser)]
    [InlineData(Target.Provisioner)]
    public async Task TheCatalog_IsReadByTwoRoles_AndWrittenByNone(string role)
    {
        await using var session = await Session.OpenAsync(role);
        Assert.Equal(7, await session.CountAsync("SELECT count(*) FROM categories"));
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM subcategories"));

        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO categories (id, code, name_ar, name_en, is_active, created_at) VALUES (uuidv7(), 'mine', 'x', 'x', true, now())"));
        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE categories SET name_en = 'x' WHERE code = 'other'"));
        Assert.Equal("42501", await session.SqlStateOfAsync("DELETE FROM categories WHERE code = 'other'"));
        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO subcategories (id, category_id, code, name_ar, name_en, is_active, created_at) " +
            "SELECT uuidv7(), id, 'mine', 'x', 'x', true, now() FROM categories WHERE code = 'hosting'"));
    }

    // Nothing for authenticator; and nothing for job_runner, whose policy names it while the grant withholds it — the
    // grant is the guard (OPEN_ITEMS 76).
    [Theory]
    [InlineData(Target.Authenticator)]
    [InlineData(Target.JobRunner)]
    public async Task TheCatalog_IsNotReadByTheOtherRoles(string role)
    {
        await using var session = await Session.OpenAsync(role);
        Assert.Equal("42501", await session.SqlStateOfAsync("SELECT 1 FROM categories"));
        Assert.Equal("42501", await session.SqlStateOfAsync("SELECT 1 FROM subcategories"));
    }

    // ---- §3.2: every field, through the API, and back; reminder_type is the server's, its one value (§2).
    [Fact]
    public async Task Creating_StoresEveryField_AndReadingReturnsThem()
    {
        using var world = await World.BootstrapAsync("a02-create");
        var p = await world.ClientAsync("P");
        var hosting = await Seed.CategoryAsync("hosting");

        var created = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions", new
        {
            client_id = p, service_name = "VPS", ends_on = "2027-03-31", category_id = hosting, provider = "Hetzner",
            cost = "12.5", currency = "JOD", billing_cycle = "monthly", auto_renew = true, login_url = "https://console.example.com/",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await world.Owner.Browser.JsonAsync(created)).GetProperty("id").GetGuid();

        var (status, s) = await world.Owner.Browser.GetAsync($"/subscriptions/{id}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(p, s.GetProperty("client_id").GetGuid());
        Assert.Equal("VPS", s.GetProperty("service_name").GetString());
        Assert.Equal("2027-03-31", s.GetProperty("ends_on").GetString());
        Assert.Equal("subscription", s.GetProperty("reminder_type").GetString());
        Assert.Equal(hosting, s.GetProperty("category_id").GetGuid());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("subcategory_id").ValueKind);
        Assert.Equal("Hetzner", s.GetProperty("provider").GetString());
        // Money is a decimal string on the wire, at the column's scale — never a JSON number (binary floating point).
        Assert.Equal("12.500", s.GetProperty("cost").GetString());
        Assert.Equal("JOD", s.GetProperty("currency").GetString());
        Assert.Equal("monthly", s.GetProperty("billing_cycle").GetString());
        Assert.True(s.GetProperty("auto_renew").GetBoolean());
        Assert.Equal("https://console.example.com/", s.GetProperty("login_url").GetString());

        // The list carries the category too, for the list screen.
        var (_, list) = await world.Owner.Browser.GetAsync("/subscriptions");
        Assert.Equal(hosting, list.GetProperty("items")[0].GetProperty("category_id").GetGuid());
    }

    // E5: the cost is optional, and zero is a value — a subscription that costs nothing still needs its reminder.
    [Fact]
    public async Task Creating_WithTheOptionalFieldsLeftOut_StoresNulls_AndZeroIsACost()
    {
        using var world = await World.BootstrapAsync("a02-optional");
        var p = await world.ClientAsync("P");

        var bare = await CreateAsync(world, Body(p, await Seed.CategoryAsync("domains")));
        var free = await CreateAsync(world, Body(p, await Seed.CategoryAsync("security"), b => { b["cost"] = "0"; b["currency"] = "USD"; }));

        var (_, s) = await world.Owner.Browser.GetAsync($"/subscriptions/{bare}");
        foreach (var field in new[] { "subcategory_id", "provider", "cost", "currency", "login_url" })
            Assert.Equal(JsonValueKind.Null, s.GetProperty(field).ValueKind);
        Assert.False(s.GetProperty("auto_renew").GetBoolean());
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i AND cost = 0 AND currency = 'USD'", ("i", free)));
    }

    // The request's rules (§14: 400 invalid_value), each refused before any write; an unknown category or subcategory
    // is invalid_reference, as any reference is.
    [Fact]
    public async Task Creating_RefusesWhatTheRulesRefuse_AndWritesNothing()
    {
        using var world = await World.BootstrapAsync("a02-refused");
        var p = await world.ClientAsync("P");
        var other = await Seed.CategoryAsync("other");

        var invalid = new Action<Dictionary<string, object?>>[]
        {
            b => b.Remove("category_id"),
            b => b.Remove("billing_cycle"),
            b => b.Remove("auto_renew"),
            b => b["billing_cycle"] = "Monthly",      // the closed catalog, letter case included (§1)
            b => b["billing_cycle"] = "weekly",
            b => { b["cost"] = "-1"; b["currency"] = "USD"; },
            b => { b["cost"] = "1.2345"; b["currency"] = "USD"; },
            b => { b["cost"] = "1e3"; b["currency"] = "USD"; },
            b => { b["cost"] = "1,5"; b["currency"] = "USD"; },
            b => { b["cost"] = "١٢"; b["currency"] = "USD"; },   // only ASCII digits reach the parser
            b => { b["cost"] = "123456789012"; b["currency"] = "USD"; },   // twelve digits: past numeric(14,3)
            b => b["cost"] = "10",                     // a cost with no currency: the reminder could not state it (E5)
            b => b["currency"] = "usd",
            b => b["currency"] = "DOLLAR",
            b => b["provider"] = "",
            b => b["login_url"] = "javascript:alert(1)",
            b => b["login_url"] = "console.example.com",
        };
        foreach (var change in invalid)
            await RefusedAsync(world, Body(p, other, change), "invalid_value");

        await RefusedAsync(world, Body(p, Guid.CreateVersion7()), "invalid_reference");
        await RefusedAsync(world, Body(p, other, b => b["subcategory_id"] = Guid.CreateVersion7()), "invalid_reference");
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE tenant_id = @t", ("t", world.TenantId)));
    }

    // The cost as a JSON number is refused: the contract is a decimal string, so no client is led through binary
    // floating point on the way to numeric(14,3).
    [Fact]
    public async Task ACostSentAsANumber_IsRefused()
    {
        using var world = await World.BootstrapAsync("a02-cost-number");
        var p = await world.ClientAsync("P");
        var response = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions",
            Body(p, await Seed.CategoryAsync("other"), b => { b["cost"] = 12.5m; b["currency"] = "USD"; }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE tenant_id = @t", ("t", world.TenantId)));
    }

    // A subcategory is a detail of its category: the two FKs hold each column alone (§3.2), and the API holds them
    // together. The catalog seeds none, so this test commits one for its length and removes it.
    [Fact]
    public async Task ASubcategory_MustBelongToTheSubscriptionsCategory()
    {
        using var world = await World.BootstrapAsync("a02-subcategory");
        var p = await world.ClientAsync("P");
        var hosting = await Seed.CategoryAsync("hosting");
        var vps = Guid.CreateVersion7();
        await World.ExecuteAsMigratorAsync(
            "INSERT INTO subcategories (id, category_id, code, name_ar, name_en, is_active, created_at) VALUES (@i, @c, 'conformance-vps', 'خادم', 'VPS', true, now())",
            ("i", vps), ("c", hosting));
        try
        {
            var (_, catalog) = await world.Owner.Browser.GetAsync("/subscriptions/categories");
            var listed = catalog.GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("code").GetString() == "hosting");
            Assert.Equal(vps, listed.GetProperty("subcategories")[0].GetProperty("id").GetGuid());

            await RefusedAsync(world, Body(p, await Seed.CategoryAsync("domains"), b => b["subcategory_id"] = vps), "invalid_reference");
            var id = await CreateAsync(world, Body(p, hosting, b => b["subcategory_id"] = vps));
            Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i AND subcategory_id = @s", ("i", id), ("s", vps)));
        }
        finally
        {
            await World.ExecuteAsMigratorAsync("DELETE FROM subscriptions WHERE subcategory_id = @s", ("s", vps));
            await World.ExecuteAsMigratorAsync("DELETE FROM subcategories WHERE id = @s", ("s", vps));
        }
    }

    // ---- The constraints layer, beneath the API: the same closed catalogs as CHECKs, and the four NOT NULLs with no
    // DEFAULT behind them (Test 28a). As app_user in an 'all' context, rolled back.
    [Theory]
    [InlineData("reminder_type", "'appointment'", "23514")]   // one value today (§2)
    [InlineData("billing_cycle", "'Monthly'", "23514")]       // no free text (§1)
    [InlineData("cost", "-1", "23514")]
    [InlineData("currency", "'usd'", "23514")]
    [InlineData("category_id", "NULL", "23502")]
    [InlineData("reminder_type", "NULL", "23502")]
    [InlineData("billing_cycle", "NULL", "23502")]
    [InlineData("auto_renew", "NULL", "23502")]
    [InlineData("category_id", "uuidv7()", "23503")]
    [InlineData("subcategory_id", "uuidv7()", "23503")]
    public async Task TheDatabase_RefusesBeneathTheApi(string column, string value, string sqlState)
    {
        var tenant = await Seed.TenantAsync(Seed.AlAmin);
        var client = await Seed.ClientAsync(Seed.AlAmin, "A");
        await using var session = await Session.OpenAsync(Target.AppUser, tenant: tenant, scopeAll: true);
        var values = new Dictionary<string, string>
        {
            ["reminder_type"] = "'subscription'", ["category_id"] = "(SELECT id FROM categories WHERE code = 'other')",
            ["billing_cycle"] = "'yearly'", ["auto_renew"] = "false", ["cost"] = "NULL", ["currency"] = "NULL", ["subcategory_id"] = "NULL",
        };
        values[column] = value;

        Assert.Equal(sqlState, await session.SqlStateOfAsync(
            $"INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at, {string.Join(", ", values.Keys)}) " +
            $"VALUES (uuidv7(), @t, @c, 'beneath', DATE '2027-06-01', now(), {string.Join(", ", values.Values)})", ("t", tenant), ("c", client)));
    }

    // ---- Editing: every field but the client, audited automatically, under the same permission and templates.
    [Fact]
    public async Task Editing_ChangesTheFields_KeepsTheClient_AndIsAudited()
    {
        using var world = await World.BootstrapAsync("a02-edit");
        var p = await world.ClientAsync("P");
        var id = await CreateAsync(world, Body(p, await Seed.CategoryAsync("domains")));
        var software = await Seed.CategoryAsync("software");

        var (status, _) = await world.Owner.Browser.PutAsync($"/subscriptions/{id}", new
        {
            service_name = "renamed", ends_on = "2028-01-15", category_id = software, provider = "Vendor", cost = "99999999999.999",
            currency = "EUR", billing_cycle = "monthly", auto_renew = true, login_url = "http://vendor.example/login",
        });
        Assert.Equal(HttpStatusCode.NoContent, status);

        var (_, s) = await world.Owner.Browser.GetAsync($"/subscriptions/{id}");
        Assert.Equal(p, s.GetProperty("client_id").GetGuid());
        Assert.Equal("renamed", s.GetProperty("service_name").GetString());
        Assert.Equal("2028-01-15", s.GetProperty("ends_on").GetString());
        Assert.Equal(software, s.GetProperty("category_id").GetGuid());
        Assert.Equal("99999999999.999", s.GetProperty("cost").GetString());   // the column's largest value, exact
        Assert.Equal("monthly", s.GetProperty("billing_cycle").GetString());
        Assert.True(s.GetProperty("auto_renew").GetBoolean());
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_type = 'subscriptions' AND entity_id = @i AND action = 'update' AND actor_id = @u",
            ("t", world.TenantId), ("i", id), ("u", world.Owner.UserId)));

        // Editing never moves a subscription to another client: the edit carries no client, and one sent anyway is
        // not read — even toward a client the caller sees.
        var q = await world.ClientAsync("Q");
        var (moved, _) = await world.Owner.Browser.PutAsync($"/subscriptions/{id}", Edit(software, b => b["client_id"] = q));
        Assert.Equal(HttpStatusCode.NoContent, moved);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i AND scope_ref_id = @p", ("i", id), ("p", p)));

        // The same rules as creating.
        var (refused, body) = await world.Owner.Browser.PutAsync($"/subscriptions/{id}", Edit(software, b => b["billing_cycle"] = "Yearly"));
        Assert.Equal(HttpStatusCode.BadRequest, refused);
        Assert.Contains("invalid_value", body);
    }

    // The second axis on reading and editing one subscription: an assigned member reads and edits their client's,
    // and another client's is refused as not permitted — the same answer as for a row that does not exist — and
    // stays unchanged. A viewer reads and does not edit.
    [Fact]
    public async Task ReadingAndEditingOne_FollowsTheScope_AndThePermission()
    {
        using var world = await World.BootstrapAsync("a02-scope");
        var p = await world.ClientAsync("P");
        var q = await world.ClientAsync("Q");
        var other = await Seed.CategoryAsync("other");
        var onP = await CreateAsync(world, Body(p, other));
        var onQ = await CreateAsync(world, Body(q, other));
        using var assigned = await world.JoinAsync("a02-scope-assigned", "operator", "assigned");
        using var viewer = await world.JoinAsync("a02-scope-viewer", "viewer", "all");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{p}", new { active = true, reason = "a02" })).Status);

        Assert.Equal(HttpStatusCode.OK, (await assigned.Browser.GetAsync($"/subscriptions/{onP}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await assigned.Browser.PutAsync($"/subscriptions/{onP}", Edit(other, b => b["service_name"] = "mine"))).Status);

        var (read, readBody) = await assigned.Browser.GetAsync($"/subscriptions/{onQ}");
        Assert.Equal(HttpStatusCode.Forbidden, read);
        Assert.Equal("not_permitted", readBody.GetProperty("error").GetString());
        var (write, writeBody) = await assigned.Browser.PutAsync($"/subscriptions/{onQ}", Edit(other, b => b["service_name"] = "not mine"));
        Assert.Equal(HttpStatusCode.Forbidden, write);
        Assert.Contains("not_permitted", writeBody);
        var (missing, _) = await assigned.Browser.GetAsync($"/subscriptions/{Guid.CreateVersion7()}");
        Assert.Equal(HttpStatusCode.Forbidden, missing);

        Assert.Equal(HttpStatusCode.OK, (await viewer.Browser.GetAsync($"/subscriptions/{onQ}")).Status);
        var (viewerWrite, viewerBody) = await viewer.Browser.PutAsync($"/subscriptions/{onQ}", Edit(other, b => b["service_name"] = "viewer"));
        Assert.Equal(HttpStatusCode.Forbidden, viewerWrite);
        Assert.Contains("not_permitted", viewerBody);

        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i AND service_name = 'mine'", ("i", onP)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i AND service_name = 'a02'", ("i", onQ)));
    }

    // Another tenant's subscription: the same refusal, from the first template.
    [Fact]
    public async Task ReadingAndEditingAnotherTenantsSubscription_IsRefused()
    {
        var x = await Seed.ScalarStringAsMigratorAsync(
            "SELECT s.id::text FROM subscriptions s JOIN tenants t ON t.id = s.tenant_id WHERE t.name = @n ORDER BY s.id LIMIT 1", ("n", Seed.Maan));
        using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await sara.GetAsync($"/subscriptions/{x}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await sara.PutAsync($"/subscriptions/{x}", Edit(await Seed.CategoryAsync("other")))).Status);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE id = @i::uuid AND service_name LIKE '% service %'", ("i", x)));
    }

    // ---- helpers

    private static Dictionary<string, object?> Body(Guid client, Guid category, Action<Dictionary<string, object?>>? change = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["client_id"] = client, ["service_name"] = "a02", ["ends_on"] = "2027-06-01", ["category_id"] = category,
            ["billing_cycle"] = "yearly", ["auto_renew"] = false,
        };
        change?.Invoke(body);
        return body;
    }

    private static Dictionary<string, object?> Edit(Guid category, Action<Dictionary<string, object?>>? change = null)
    {
        var body = Body(Guid.Empty, category, change);
        body.Remove("client_id");
        return body;
    }

    private static async Task<Guid> CreateAsync(World world, Dictionary<string, object?> body)
    {
        var response = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await world.Owner.Browser.JsonAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task RefusedAsync(World world, Dictionary<string, object?> body, string code)
    {
        var response = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions", body);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{JsonSerializer.Serialize(body)} → {(int)response.StatusCode}");
        Assert.Equal(code, (await world.Owner.Browser.JsonAsync(response)).GetProperty("error").GetString());
    }
}

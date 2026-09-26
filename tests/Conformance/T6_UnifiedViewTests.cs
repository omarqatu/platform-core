using System.Net;
using System.Text.Json;
using Npgsql;

namespace Conformance;

// PROOF_SPEC v1.3 T6 — the cross-tenant unified view (PLATFORM_CORE v1.16 §6): GET /unified/subscriptions, the
// caller's own memberships, a transaction per tenant, a cursor per tenant, a k-way merge. T6.1 reads the seed
// contract (Omar: 'all' in Al-Amin, 'assigned' in Maan); the rest build tenants of their own (World), their clients
// and subscriptions set up as migrator, like the seed's own.
public class T6_UnifiedViewTests
{
    // ---- T6.1 — scope 'all' in one tenant and 'assigned' in another → the merged result carries the scope per tenant,
    // with total_count for the first tenant only.
    [Fact]
    public async Task T6_1_ScopePerTenant_TotalCountForTheAllTenantOnly()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var maan = await Seed.TenantAsync(Seed.Maan);
        using var omar = await Browser.SignedInAsync("omar");

        var page = await PageAsync(omar, limit: 100);

        var tenants = page.GetProperty("tenants").EnumerateArray().ToDictionary(t => t.GetProperty("tenant_id").GetGuid());
        Assert.Equal(2, tenants.Count);
        Assert.Equal("all", tenants[alAmin].GetProperty("scope_mode").GetString());
        Assert.Equal(20, tenants[alAmin].GetProperty("total_count").GetInt64());
        Assert.Equal(20, tenants[alAmin].GetProperty("visible_count").GetInt64());
        Assert.Equal("assigned", tenants[maan].GetProperty("scope_mode").GetString());
        Assert.True(tenants[maan].TryGetProperty("total_count", out var hidden));
        Assert.Equal(JsonValueKind.Null, hidden.ValueKind);
        Assert.Equal(0, tenants[maan].GetProperty("visible_count").GetInt64());
        // Omar's Maan scope ('assigned', nothing assigned) is not carried from Al-Amin: no Maan row in the merge.
        Assert.All(page.GetProperty("items").EnumerateArray(), i => Assert.Equal(alAmin, i.GetProperty("tenant_id").GetGuid()));
        Assert.Equal(20, page.GetProperty("items").GetArrayLength());
    }

    // ---- T6.2 — three consecutive pages over interleaved data across three tenants → no duplication, no loss, in
    // the reference order computed by PostgreSQL.
    [Fact]
    public async Task T6_2_ThreeConsecutivePages_InterleavedAcrossTenants_NoDuplicationNoLoss()
    {
        using var w1 = await World.BootstrapAsync("t6-2-a");
        using var w2 = await World.BootstrapAsync("t6-2-b");
        using var w3 = await World.BootstrapAsync("t6-2-c");
        await w2.JoinExistingAsync(w1.Owner, "viewer", "all");
        await w3.JoinExistingAsync(w1.Owner, "viewer", "all");
        var start = new DateOnly(2028, 1, 1);
        var worlds = new[] { w1, w2, w3 };
        for (var i = 0; i < worlds.Length; i++)
        {
            // Tenant i ends on days i, i+3, i+6, … — every page interleaves the three; plus same-day ties across tenants.
            var client = await worlds[i].ClientAsync("C");
            await worlds[i].SubscriptionsAsync(client, Enumerable.Range(0, 5).Select(k => start.AddDays(i + 3 * k)).Append(start.AddDays(7)).ToArray());
        }
        var reference = await ReferenceAsync(worlds.Select(w => w.TenantId).ToArray());

        var (pages, all) = await AllPagesAsync(w1.Owner.Browser, limit: 5);

        Assert.True(pages.Count >= 3);
        Assert.Equal(reference.Take(15), pages.Take(3).SelectMany(p => p));
        Assert.Equal(reference, all);
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(pages, p => Assert.InRange(p.Count, 1, 5));
    }

    // ---- T6.3 — a failure in one tenant → the rest returns, and the failing tenant's context does not leak into the
    // next: the middle tenant's membership loses its membership_scope row (Rule 7: its resolution throws); the tenants
    // before and after it return with their own scope.
    [Fact]
    public async Task T6_3_AFailingTenant_TheRestReturns_NoContextLeaks()
    {
        using var w1 = await World.BootstrapAsync("t6-3-a");
        using var w2 = await World.BootstrapAsync("t6-3-b");
        using var w3 = await World.BootstrapAsync("t6-3-c");
        var failing = await w2.JoinExistingAsync(w1.Owner, "viewer", "all");
        var assignedIn3 = await w3.JoinExistingAsync(w1.Owner, "viewer", "assigned");
        foreach (var w in new[] { w1, w2, w3 })
        {
            var p = await w.ClientAsync("P");
            var q = await w.ClientAsync("Q");
            await w.SubscriptionsAsync(p, new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 2));
            await w.SubscriptionsAsync(q, new DateOnly(2028, 2, 3));
            if (w == w3)
                Assert.Equal(HttpStatusCode.NoContent, (await w3.Owner.Browser.PutAsync(
                    $"/scope/memberships/{assignedIn3}/assignments/{p}", new { active = true, reason = "t6-3" })).Status);
        }
        await World.ExecuteAsMigratorAsync("DELETE FROM membership_scope WHERE membership_id = @m", ("m", failing));

        var page = await PageAsync(w1.Owner.Browser, limit: 100);

        var tenants = page.GetProperty("tenants").EnumerateArray().ToDictionary(t => t.GetProperty("tenant_id").GetGuid());
        Assert.Equal("failed", tenants[w2.TenantId].GetProperty("status").GetString());
        Assert.Equal("ok", tenants[w1.TenantId].GetProperty("status").GetString());
        Assert.Equal("all", tenants[w1.TenantId].GetProperty("scope_mode").GetString());
        Assert.Equal("ok", tenants[w3.TenantId].GetProperty("status").GetString());
        Assert.Equal("assigned", tenants[w3.TenantId].GetProperty("scope_mode").GetString());
        Assert.Equal(JsonValueKind.Null, tenants[w3.TenantId].GetProperty("total_count").ValueKind);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count(i => i.GetProperty("tenant_id").GetGuid() == w1.TenantId));
        Assert.Equal(2, items.Count(i => i.GetProperty("tenant_id").GetGuid() == w3.TenantId));   // P only: assigned
        Assert.DoesNotContain(items, i => i.GetProperty("tenant_id").GetGuid() == w2.TenantId);
    }

    // ---- T6.4 — a member in 25 tenants → progressive paging, no memory collapse: every page bounded by its limit,
    // every tenant declared, and the pages together exactly the reference order.
    [Fact]
    public async Task T6_4_AMemberIn25Tenants_ProgressivePaging()
    {
        var worlds = new List<World>();
        try
        {
            for (var i = 0; i < 25; i++)
                worlds.Add(await World.BootstrapAsync($"t6-4-{i}"));
            var member = worlds[0].Owner;
            for (var i = 1; i < 25; i++)
                await worlds[i].JoinExistingAsync(member, "viewer", "all");
            for (var i = 0; i < 25; i++)
            {
                var client = await worlds[i].ClientAsync("C");
                await worlds[i].SubscriptionsAsync(client, new DateOnly(2029, 1, 1).AddDays(i % 7), new DateOnly(2029, 3, 1).AddDays(-(i % 5)));
            }
            var reference = await ReferenceAsync(worlds.Select(w => w.TenantId).ToArray());

            var (pages, all) = await AllPagesAsync(member.Browser, limit: 7, declaredTenants: 25);

            Assert.Equal(50, reference.Count);
            Assert.Equal(reference, all);
            Assert.Equal((50 + 6) / 7, pages.Count);
            Assert.All(pages, p => Assert.InRange(p.Count, 1, 7));
        }
        finally
        {
            foreach (var w in worlds)
                w.Dispose();
        }
    }

    // ---- helpers

    private static async Task<JsonElement> PageAsync(Browser browser, int limit, string? cursor = null)
    {
        var (status, body) = await browser.GetAsync($"/unified/subscriptions?limit={limit}" + (cursor is null ? "" : "&cursor=" + cursor));
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    private static async Task<(List<List<(Guid, Guid)>> Pages, List<(Guid, Guid)> All)> AllPagesAsync(Browser browser, int limit,
        int? declaredTenants = null)
    {
        var pages = new List<List<(Guid, Guid)>>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(browser, limit, cursor);
            if (declaredTenants is { } n)
                Assert.Equal(n, page.GetProperty("tenants").GetArrayLength());
            Assert.DoesNotContain(page.GetProperty("tenants").EnumerateArray(), t => t.GetProperty("status").GetString() is "failed" or "not_permitted");
            pages.Add(page.GetProperty("items").EnumerateArray()
                .Select(i => (i.GetProperty("tenant_id").GetGuid(), i.GetProperty("id").GetGuid())).ToList());
            cursor = page.GetProperty("next_cursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("next_cursor").GetString();
            Assert.Equal(cursor is not null, page.GetProperty("has_more").GetBoolean());
            Assert.True(pages.Count < 100, "paging does not end");
        } while (cursor is not null);
        return (pages, pages.SelectMany(p => p).ToList());
    }

    // The reference order, computed by PostgreSQL as migrator: (ends_on, id) across the tenants.
    private static async Task<List<(Guid, Guid)>> ReferenceAsync(Guid[] tenants)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id, id FROM subscriptions WHERE tenant_id = ANY (@t) ORDER BY ends_on, id", connection);
        command.Parameters.AddWithValue("t", tenants);
        var rows = new List<(Guid, Guid)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add((reader.GetGuid(0), reader.GetGuid(1)));
        return rows;
    }
}

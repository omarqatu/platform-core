using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Conformance;

// GET /subscriptions?status= (the project owner's item 4): one lifecycle status, or every status when absent. It
// narrows the rows the caller already sees; it never decides who sees what, which stays RLS's. So the declaration is
// counted after it (PROOF_SPEC spec item m: visible_count after every constraint together — scope, permission,
// status, search; total_count the same but for scope, under scope_all only, null otherwise).
// A tenant of the test's own: client P with one active, one renewed (and its active renewal) and one cancelled
// subscription; client Q with one active one. The owner is 'all'; an operator is 'assigned' to P alone.
public class T5_SubscriptionStatusFilterTests
{
    private sealed record Fixture(World World, Member Assigned, Guid P, Guid Q, Guid Renewed, Guid Renewal, Guid Cancelled, Guid ActiveOfP, Guid ActiveOfQ)
        : IDisposable
    {
        public void Dispose()
        {
            Assigned.Dispose();
            World.Dispose();
        }
    }

    private static async Task<Guid> CreateAsync(World world, Guid client, string service)
    {
        var created = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions", new
        {
            client_id = client, service_name = service, ends_on = "2027-06-01", category_id = await Seed.CategoryAsync("hosting"),
            billing_cycle = "yearly", auto_renew = false,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await world.Owner.Browser.JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<Fixture> EveryStatusAsync(string label)
    {
        var world = await World.BootstrapAsync(label);
        var p = await world.ClientAsync("P");
        var q = await world.ClientAsync("Q");
        var activeOfP = await CreateAsync(world, p, "Active P");
        var renewed = await CreateAsync(world, p, "Renewed P");
        var renew = await world.Owner.Browser.Client.PostAsJsonAsync($"/subscriptions/{renewed}/renew", new { ends_on = "2028-06-01" });
        Assert.Equal(HttpStatusCode.Created, renew.StatusCode);
        var renewal = (await world.Owner.Browser.JsonAsync(renew)).GetProperty("id").GetGuid();
        var cancelled = await CreateAsync(world, p, "Cancelled P");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.Client.PostAsJsonAsync($"/subscriptions/{cancelled}/cancel", new { })).StatusCode);
        var activeOfQ = await CreateAsync(world, q, "Active Q");

        var assigned = await world.JoinAsync($"{label}-op", "operator", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{p}", new { active = true, reason = label })).Status);
        return new Fixture(world, assigned, p, q, renewed, renewal, cancelled, activeOfP, activeOfQ);
    }

    private static async Task<JsonElement> ListAsync(Browser browser, string path)
    {
        var (status, body) = await browser.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    private static Guid[] Ids(JsonElement body) =>
        body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).Order().ToArray();

    [Fact]
    public async Task Absent_IsEveryStatus_AsBefore()
    {
        using var f = await EveryStatusAsync("status-absent");

        var body = await ListAsync(f.World.Owner.Browser, "/subscriptions?limit=200");

        Assert.Equal(new[] { f.ActiveOfP, f.Renewed, f.Renewal, f.Cancelled, f.ActiveOfQ }.Order(), Ids(body));
        Assert.Equal(5, body.GetProperty("visible_count").GetInt64());
        Assert.Equal(5, body.GetProperty("total_count").GetInt64());
    }

    [Theory]
    [InlineData("active")]
    [InlineData("renewed")]
    [InlineData("cancelled")]
    public async Task EachStatus_ReturnsThatStatusAlone_AndAllCountsItAlone(string status)
    {
        using var f = await EveryStatusAsync($"status-{status}");
        Guid[] expected = status switch
        {
            "active" => [f.ActiveOfP, f.Renewal, f.ActiveOfQ],
            "renewed" => [f.Renewed],
            _ => [f.Cancelled],
        };

        var body = await ListAsync(f.World.Owner.Browser, $"/subscriptions?status={status}&limit=200");

        Assert.Equal("all", body.GetProperty("scope_mode").GetString());
        Assert.Equal(expected.Order(), Ids(body));
        Assert.All(body.GetProperty("items").EnumerateArray(), s => Assert.Equal(status, s.GetProperty("status").GetString()));
        // Both counts are after the filter: under scope_all the scope removes nothing, so they are the same number.
        Assert.Equal(expected.Length, body.GetProperty("visible_count").GetInt64());
        Assert.Equal(expected.Length, body.GetProperty("total_count").GetInt64());
    }

    // The filter narrows what RLS already lets through; it never widens it. The assigned member sees P's rows of that
    // status, not Q's, the count is of those, and there is still no total.
    [Theory]
    [InlineData("active", 2)]
    [InlineData("renewed", 1)]
    [InlineData("cancelled", 1)]
    public async Task Assigned_SeesTheStatusWithinTheirScope_CountedAfterIt_AndNoTotal(string status, int count)
    {
        using var f = await EveryStatusAsync($"status-assigned-{status}");

        var body = await ListAsync(f.Assigned.Browser, $"/subscriptions?status={status}&limit=200");

        Assert.Equal("assigned", body.GetProperty("scope_mode").GetString());
        Assert.Equal(count, body.GetProperty("visible_count").GetInt64());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("total_count").ValueKind);
        Assert.Equal(count, Ids(body).Length);
        Assert.All(body.GetProperty("items").EnumerateArray(), s =>
        {
            Assert.Equal(f.P, s.GetProperty("client_id").GetGuid());
            Assert.Equal(status, s.GetProperty("status").GetString());
        });
    }

    [Fact]
    public async Task ThePageAndHasMore_AreAfterTheFilter()
    {
        using var f = await EveryStatusAsync("status-page");

        var first = await ListAsync(f.World.Owner.Browser, "/subscriptions?status=active&limit=2");
        var rest = await ListAsync(f.World.Owner.Browser, "/subscriptions?status=active&limit=2&offset=2");

        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.True(first.GetProperty("has_more_in_scope").GetBoolean());
        Assert.Equal(1, rest.GetProperty("items").GetArrayLength());
        Assert.False(rest.GetProperty("has_more_in_scope").GetBoolean());
        Assert.Equal(new[] { f.ActiveOfP, f.Renewal, f.ActiveOfQ }.Order(), Ids(first).Concat(Ids(rest)).Order());
    }

    // One value from a known set, exactly. On ASP.NET Core 10 a repeated parameter binds to `string? status` as the
    // values joined by a comma ("active,renewed") and an empty one as "" — each refused like any unknown value.
    [Theory]
    [InlineData("?status=")]
    [InlineData("?status=expired")]
    [InlineData("?status=ACTIVE")]
    [InlineData("?status=active%20")]
    [InlineData("?status=active,renewed")]
    [InlineData("?status=active&status=renewed")]
    [InlineData("?status=active&status=active")]
    public async Task AnythingButOneKnownStatus_Is400InvalidValue(string query)
    {
        using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin); // a read: the seed is enough

        var (status, body) = await sara.GetAsync($"/subscriptions{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_value", body.GetProperty("error").GetString());
    }
}

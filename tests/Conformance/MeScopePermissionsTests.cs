using System.Net;
using System.Text.Json;

namespace Conformance;

// GET /me/scope's permissions — not a PROOF_SPEC criterion: added for the interface to hide what the server would
// refuse (OPEN_ITEMS 77), approved by the project owner. Every code the caller holds in the active tenant, sorted
// ordinally, resolved on the request's transaction like every permission check (3.5/6, 5) — never from the session,
// a header or the query. A courtesy to the screens: each endpoint still requires its permission on its own request.
public class MeScopePermissionsTests
{
    // Exactly what the database says the membership holds — as migrator, outside every tenant context.
    [Theory]
    [InlineData("omar", Seed.AlAmin)]
    [InlineData("sara", Seed.AlAmin)]
    [InlineData("khaled", Seed.AlAmin)]
    [InlineData("layla", Seed.AlAmin)]
    [InlineData("rami", Seed.AlAmin)]
    [InlineData("omar", Seed.Maan)]
    [InlineData("nour", Seed.Maan)]
    public async Task ThePermissions_AreExactlyTheMembershipsOwn_Sorted(string username, string tenant)
    {
        using var browser = await Browser.SignedInAsync(username, tenant);

        var held = await PermissionsAsync(browser);

        Assert.Equal(await GroundTruthAsync(username, tenant), held);
    }

    // The case the interface gates on: a viewer reads and writes nothing.
    [Fact]
    public async Task AViewer_HoldsRead_AndNoWrite()
    {
        using var layla = await Browser.SignedInAsync("layla", Seed.AlAmin);

        var held = await PermissionsAsync(layla);

        Assert.Contains("subscriptions.read", held);
        Assert.DoesNotContain("subscriptions.write", held);
        Assert.DoesNotContain("subscriptions.clients.write", held);
        Assert.DoesNotContain("subscriptions.clients.manage", held);
    }

    // An operator edits clients and does not disable them (Amendment 01 §4.2).
    [Fact]
    public async Task AnOperator_WritesClients_AndDoesNotManageThem()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var held = await PermissionsAsync(khaled);

        Assert.Contains("subscriptions.write", held);
        Assert.Contains("subscriptions.clients.write", held);
        Assert.DoesNotContain("subscriptions.clients.manage", held);
    }

    // The same person in two tenants: the permissions are the active tenant's membership's, not the person's.
    [Fact]
    public async Task ThePermissions_AreTheActiveTenants()
    {
        using var inAlAmin = await Browser.SignedInAsync("omar", Seed.AlAmin);
        using var inMaan = await Browser.SignedInAsync("omar", Seed.Maan);

        Assert.Contains("subscriptions.clients.manage", await PermissionsAsync(inAlAmin));   // owner
        Assert.DoesNotContain("subscriptions.clients.manage", await PermissionsAsync(inMaan)); // operator
    }

    // Nothing a request carries adds a permission (as Test 17-c for the scope variables).
    [Fact]
    public async Task APermissionInTheQueryOrAHeader_HasNoEffect()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var (status, body) = await khaled.GetAsync("/me/scope", request =>
        {
            request.RequestUri = new Uri("/me/scope?permissions=subscriptions.clients.manage", UriKind.Relative);
            request.Headers.Add("X-Permissions", "subscriptions.clients.manage");
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.DoesNotContain("subscriptions.clients.manage", Read(body));
    }

    private static async Task<List<string>> PermissionsAsync(Browser browser)
    {
        var (status, body) = await browser.GetAsync("/me/scope");
        Assert.Equal(HttpStatusCode.OK, status);
        return Read(body);
    }

    private static List<string> Read(JsonElement body) =>
        body.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).ToList();

    private static async Task<List<string>> GroundTruthAsync(string username, string tenant)
    {
        var codes = await Seed.ScalarStringAsMigratorAsync(
            """
            SELECT coalesce(string_agg(DISTINCT p.code COLLATE "C", ',' ORDER BY p.code COLLATE "C"), '')
            FROM memberships m
            JOIN users u ON u.id = m.user_id
            JOIN tenants t ON t.id = m.tenant_id
            JOIN membership_roles mr ON mr.membership_id = m.id
            JOIN role_permissions rp ON rp.role_id = mr.role_id
            JOIN permissions p ON p.id = rp.permission_id
            WHERE u.username = @username AND t.name = @tenant
            """,
            ("username", username), ("tenant", tenant));
        return codes.Length == 0 ? [] : codes.Split(',').ToList();
    }
}

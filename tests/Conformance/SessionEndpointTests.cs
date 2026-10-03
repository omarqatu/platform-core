using System.Net;

namespace Conformance;

// The session endpoints the web interface's identity screens use — not PROOF_SPEC criteria: added with those screens,
// approved by the project owner. GET /me is the tenant-selection path (app.user_id alone, T3.7): the user's username,
// and the active tenant only while the user can still enter it. POST /auth/logout ends the browser's session.
// POST /tenants/deselect leaves the active tenant and keeps the session (OPEN_ITEMS 38, decided by the project owner).
public class SessionEndpointTests
{
    [Fact]
    public async Task Me_BeforeSelection_TheUserAndNoTenant()
    {
        using var omar = await Browser.SignedInAsync("omar");

        var (status, me) = await omar.GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("omar", me.GetProperty("username").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("active_tenant").ValueKind);
    }

    [Fact]
    public async Task Me_AfterSelection_TheTenant()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.Maan);

        var (_, me) = await omar.GetAsync("/me");

        var tenant = me.GetProperty("active_tenant");
        Assert.Equal(await Seed.TenantAsync(Seed.Maan), tenant.GetProperty("tenant_id").GetGuid());
        Assert.Equal(Seed.Maan, tenant.GetProperty("name").GetString());
    }

    // A membership disabled during the session: the cookie still holds the tenant, /me no longer names it.
    [Fact]
    public async Task Me_MembershipDisabledDuringTheSession_NoTenant()
    {
        using var world = await World.BootstrapAsync("me-disabled");
        var viewer = await world.JoinAsync("me-viewer", "viewer", "all");
        Assert.Equal(world.TenantId, (await viewer.Browser.GetAsync("/me")).Body.GetProperty("active_tenant").GetProperty("tenant_id").GetGuid());

        var disabled = await world.Owner.Browser.PutAsync($"/members/{viewer.MembershipId}/status", new { status = "disabled" });
        Assert.Equal(HttpStatusCode.NoContent, disabled.Status);

        var (status, me) = await viewer.Browser.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("active_tenant").ValueKind);
    }

    [Fact]
    public async Task Me_WithoutSession_401()
    {
        using var anonymous = new Browser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Client.GetAsync("/me")).StatusCode);
    }

    // After logout, the browser's session is gone: every request is 401.
    [Fact]
    public async Task Logout_ThenEveryRequestIs401()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);

        var logout = await omar.Client.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("session=;"));
        foreach (var path in new[] { "/me", "/tenants", "/subscriptions" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await omar.Client.GetAsync(path)).StatusCode);
    }

    // No session is needed to log out: an expired one logs out the same way.
    [Fact]
    public async Task Logout_WithoutSession_204()
    {
        using var anonymous = new Browser();

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.Client.PostAsync("/auth/logout", null)).StatusCode);
    }

    // Logout is an unsafe request: from a foreign origin it is refused, and the session stays.
    [Fact]
    public async Task Logout_FromAForeignOrigin_IsRefused_AndTheSessionStays()
    {
        using var omar = await Browser.SignedInAsync("omar");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
        request.Headers.Add("Origin", "https://attacker.example");

        var response = await omar.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await omar.Client.GetAsync("/me")).StatusCode);
    }

    // Deselect: the session stays, the tenant goes — /me names none, and the tenant's endpoints refuse.
    [Fact]
    public async Task Deselect_TheSessionStays_WithNoActiveTenant()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);
        Assert.Equal(HttpStatusCode.OK, (await omar.GetAsync("/me/scope")).Status);

        var deselect = await omar.Client.PostAsync("/tenants/deselect", null);

        Assert.Equal(HttpStatusCode.NoContent, deselect.StatusCode);
        var (status, me) = await omar.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("omar", me.GetProperty("username").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("active_tenant").ValueKind);
        var (scopeStatus, scope) = await omar.GetAsync("/me/scope");
        Assert.Equal(HttpStatusCode.Conflict, scopeStatus);
        Assert.Equal("no_active_tenant", scope.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Deselect_WithoutSession_401()
    {
        using var anonymous = new Browser();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.Client.PostAsync("/tenants/deselect", null)).StatusCode);
    }

    // An unsafe request: from a foreign origin it is refused, and the tenant stays.
    [Fact]
    public async Task Deselect_FromAForeignOrigin_IsRefused_AndTheTenantStays()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/tenants/deselect");
        request.Headers.Add("Origin", "https://attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, (await omar.Client.SendAsync(request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await omar.GetAsync("/me/scope")).Status);
    }
}

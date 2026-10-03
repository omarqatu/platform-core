using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Conformance;

// CSRF (OPEN_ITEMS 28, closed by the CSRF PR) — not a PROOF_SPEC criterion: a project-owner decision, tested here
// as [B] through HTTP. Every unsafe request (POST, PUT, PATCH, DELETE) needs an Origin equal to an allowed origin
// and X-Requested-With: platform-web, or it is refused: 403, csrf_rejected — login and invitation acceptance
// included. The harness's own requests carry both (Browser); these tests send their own headers, from a Browser
// that sends neither by default.
public class CsrfTests
{
    private const string Rejected = "csrf_rejected";

    // A foreign origin, and a sibling subdomain of the allowed one: the same site, which SameSite=Strict lets through.
    private static readonly string Foreign = "https://attacker.example";
    private static string Sibling
    {
        get
        {
            var allowed = new Uri(Browser.Origin);
            return $"{allowed.Scheme}://evil.{allowed.Authority}";
        }
    }

    public static TheoryData<string?> RefusedOrigins => new() { null, Foreign, Sibling };

    // No Origin, a foreign Origin, a sibling subdomain's Origin — each with X-Requested-With: refused.
    [Theory]
    [MemberData(nameof(RefusedOrigins))]
    public async Task UnsafeRequest_WithoutTheAllowedOrigin_IsRefused(string? origin)
    {
        using var browser = await SignedInWithoutHeadersAsync("sara");
        var response = await SelectAsync(browser, origin, Browser.RequestedWith);

        await AssertRejectedAsync(response);
    }

    // The allowed Origin, without X-Requested-With: refused.
    [Fact]
    public async Task UnsafeRequest_WithoutXRequestedWith_IsRefused()
    {
        using var browser = await SignedInWithoutHeadersAsync("sara");
        var response = await SelectAsync(browser, Browser.Origin, requestedWith: null);

        await AssertRejectedAsync(response);
    }

    // Every unsafe method, with neither header: refused, before anything else answers.
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task EveryUnsafeMethod_WithNeitherHeader_IsRefused(string method)
    {
        using var browser = await SignedInWithoutHeadersAsync("sara");
        var response = await browser.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), "/me/leave"));

        await AssertRejectedAsync(response);
    }

    // A body-less endpoint that changes state: leaving the tenant from a foreign origin is refused, and the membership
    // is untouched. A viewer of a world of the test's own — whose departure would otherwise succeed (consent prevails).
    [Fact]
    public async Task Leave_FromAForeignOrigin_IsRefused_AndTheMembershipIsUnchanged()
    {
        using var world = await World.BootstrapAsync("csrf-leave");
        var viewer = await world.JoinAsync("csrf-viewer", "viewer", "assigned");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/me/leave");
        request.Headers.Add("Origin", Foreign);   // replaces the harness's own; X-Requested-With stays
        var response = await viewer.Browser.Client.SendAsync(request);

        await AssertRejectedAsync(response);
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM memberships WHERE id = @m AND status = 'active'", ("m", viewer.MembershipId)));
    }

    // Login CSRF: a login from a foreign origin is refused, and no session cookie is set.
    [Fact]
    public async Task Login_FromAForeignOrigin_IsRefused_AndSetsNoSession()
    {
        using var browser = new Browser(platformHeaders: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new { username = "khaled", password = Browser.PasswordOf("khaled") }),
        };
        request.Headers.Add("Origin", Foreign);
        request.Headers.Add(Browser.RequestedWithHeader, Browser.RequestedWith);
        var response = await browser.Client.SendAsync(request);

        await AssertRejectedAsync(response);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("session=")));
        Assert.Empty(browser.Cookies.GetCookies(Browser.BaseUrl));
    }

    // Invitation acceptance has no session either: refused from a foreign origin all the same.
    [Fact]
    public async Task InvitationAcceptance_FromAForeignOrigin_IsRefused()
    {
        using var browser = new Browser(platformHeaders: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/invitations/accept")
        {
            Content = JsonContent.Create(new { tenant_id = Guid.CreateVersion7(), token = "any" }),
        };
        request.Headers.Add("Origin", Foreign);
        request.Headers.Add(Browser.RequestedWithHeader, Browser.RequestedWith);

        await AssertRejectedAsync(await browser.Client.SendAsync(request));
    }

    // GET is not affected: with neither header, or with a foreign Origin, it answers as it always has.
    [Fact]
    public async Task Get_IsNotAffected()
    {
        using var browser = await SignedInWithoutHeadersAsync("sara");

        Assert.Equal(HttpStatusCode.OK, (await browser.Client.GetAsync("/tenants")).StatusCode);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, "/tenants");
        foreign.Headers.Add("Origin", Foreign);
        Assert.Equal(HttpStatusCode.OK, (await browser.Client.SendAsync(foreign)).StatusCode);
    }

    /// <summary>Signed in through the harness's headers; the returned browser itself sends none.</summary>
    private static async Task<Browser> SignedInWithoutHeadersAsync(string username)
    {
        using var signedIn = await Browser.SignedInAsync(username);
        var bare = new Browser(platformHeaders: false);
        foreach (Cookie cookie in signedIn.Cookies.GetCookies(Browser.BaseUrl))
            bare.Cookies.Add(cookie);
        return bare;
    }

    private static async Task<HttpResponseMessage> SelectAsync(Browser browser, string? origin, string? requestedWith)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/tenants/{await Seed.TenantAsync(Seed.AlAmin)}/select");
        if (origin is not null)
            request.Headers.Add("Origin", origin);
        if (requestedWith is not null)
            request.Headers.Add(Browser.RequestedWithHeader, requestedWith);
        return await browser.Client.SendAsync(request);
    }

    private static async Task AssertRejectedAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(Rejected, body.RootElement.GetProperty("error").GetString());
    }
}

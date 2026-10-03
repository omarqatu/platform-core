using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Conformance;

// The path space (OPEN_ITEMS 37, decided by the project owner) — not a PROOF_SPEC criterion. The API lives under
// Api:BasePath (/api) and only there: a bare API path is gone, an unknown path under /api is a real 404, never the
// interface; every other page path is the interface (index.html), except the one documented route outside both, the
// original T8 screen. These requests are sent as written (Browser.AsIs), never under the base path.
public class ApiPathTests
{
    private const string InterfaceMarker = "<div id=\"root\">";

    // A bare API path, asked for JSON by a signed-in user: 404 — not the API's 200, not the interface's HTML.
    [Fact]
    public async Task BareApiPath_Json_404()
    {
        using var omar = await Browser.SignedInAsync("omar");

        var response = await SendAsIs(omar, HttpMethod.Get, "/tenants", "application/json");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(InterfaceMarker, await response.Content.ReadAsStringAsync());
        // The same request under the base path is the API's.
        Assert.Equal(HttpStatusCode.OK, (await omar.Client.GetAsync("/tenants")).StatusCode);
    }

    // An unknown path under /api, even asked for HTML: a real 404, never index.html.
    [Fact]
    public async Task UnknownPathUnderApi_404_NeverTheInterface()
    {
        using var omar = await Browser.SignedInAsync("omar");

        var response = await SendAsIs(omar, HttpMethod.Get, Browser.BasePath + "/does-not-exist", "text/html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(InterfaceMarker, await response.Content.ReadAsStringAsync());
    }

    // A page path of the interface, asked for HTML: the interface.
    [Fact]
    public async Task InterfacePath_Html_TheInterface()
    {
        using var anonymous = new Browser();

        var response = await SendAsIs(anonymous, HttpMethod.Get, "/app/anything", "text/html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(InterfaceMarker, await response.Content.ReadAsStringAsync());
    }

    // The documented exception: the original T8 screen, at its own path, as it was — the server's page, not the
    // interface's.
    [Fact]
    public async Task TheOriginalT8Screen_AtItsPath_AsItWas()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var response = await SendAsIs(khaled, HttpMethod.Get, "/subscriptions/screen", "text/html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Matches("<html[^>]*\\blang=\"ar\"[^>]*\\bdir=\"rtl\"", html);
        Assert.Contains("<p class=\"scope\" data-scope-mode=\"assigned\">", html);
        Assert.DoesNotContain(InterfaceMarker, html);
    }

    // A bare unsafe path. CsrfProtection runs before routing, on every path: with the interface's headers the request
    // passes it and reaches routing, which matches nothing — 404; without them it is refused before routing — 403
    // csrf_rejected. Either way the tenant is not selected.
    [Fact]
    public async Task BareApiPath_Post_404_OrRefusedByCsrfFirst()
    {
        using var omar = await Browser.SignedInAsync("omar");
        var path = $"/tenants/{await Seed.TenantAsync(Seed.AlAmin)}/select";

        var withHeaders = await SendAsIs(omar, HttpMethod.Post, path, "application/json");
        Assert.Equal(HttpStatusCode.NotFound, withHeaders.StatusCode);

        using var bare = new Browser(platformHeaders: false);
        foreach (Cookie cookie in omar.Cookies.GetCookies(Browser.BaseUrl))
            bare.Cookies.Add(cookie);
        var withoutHeaders = await SendAsIs(bare, HttpMethod.Post, path, "application/json");
        Assert.Equal(HttpStatusCode.Forbidden, withoutHeaders.StatusCode);
        using var body = JsonDocument.Parse(await withoutHeaders.Content.ReadAsStringAsync());
        Assert.Equal("csrf_rejected", body.RootElement.GetProperty("error").GetString());

        var (_, me) = await omar.GetAsync("/me");
        Assert.Equal(JsonValueKind.Null, me.GetProperty("active_tenant").ValueKind);
    }

    private static Task<HttpResponseMessage> SendAsIs(Browser browser, HttpMethod method, string path, string accept)
    {
        var request = Browser.AsIs(new HttpRequestMessage(method, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return browser.Client.SendAsync(request);
    }
}

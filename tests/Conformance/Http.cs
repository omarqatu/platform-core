using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Conformance;

/// <summary>
/// One browser against the implementation's HTTP surface (from T3): its own cookie jar, so each instance is one
/// session. The base URL and the seed users' passwords come from configuration (PROOF_SPEC 6); the endpoints and
/// their contracts are the ones named in T3.
/// </summary>
public sealed class Browser : IDisposable
{
    private static readonly IConfiguration Config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddEnvironmentVariables()
        .Build();

    public static Uri BaseUrl => new(Config["Api:BaseUrl"] ?? throw new InvalidOperationException("Api:BaseUrl is not configured."));

    /// <summary>The seed contract's test-only password for a seed user (tests/seed/seed-contract.sql header).</summary>
    public static string PasswordOf(string username) =>
        string.Format(Config["Seed:PasswordFormat"] ?? throw new InvalidOperationException("Seed:PasswordFormat is not configured."), username);

    public CookieContainer Cookies { get; } = new();
    public HttpClient Client { get; }

    public Browser()
    {
        Client = new HttpClient(new HttpClientHandler { CookieContainer = Cookies, UseCookies = true }) { BaseAddress = BaseUrl };
    }

    public Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        Client.PostAsJsonAsync("/auth/login", new { username, password });

    /// <summary>Logs a seed user in with their seed password, and fails the test if that does not succeed.</summary>
    public static async Task<Browser> SignedInAsync(string username, string? tenant = null)
    {
        var browser = new Browser();
        var login = await browser.LoginAsync(username, PasswordOf(username));
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        if (tenant is not null)
        {
            var select = await browser.SelectAsync(await Seed.TenantAsync(tenant));
            Assert.Equal(HttpStatusCode.NoContent, select.StatusCode);
        }
        return browser;
    }

    public Task<HttpResponseMessage> SelectAsync(Guid tenantId) =>
        Client.PostAsync($"/tenants/{tenantId}/select", null);

    public async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path, Action<HttpRequestMessage>? configure = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        configure?.Invoke(request);
        var response = await Client.SendAsync(request);
        return (response.StatusCode, await JsonAsync(response));
    }

    public async Task<(HttpStatusCode Status, string Body)> PutAsync(string path, object body)
    {
        var response = await Client.PutAsJsonAsync(path, body);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The caller's scope as the implementation resolves it on this request.</summary>
    public async Task<(string Mode, bool CanManage, List<Guid> Assignments)> ScopeAsync(Action<HttpRequestMessage>? configure = null)
    {
        var (status, body) = await GetAsync("/me/scope", configure);
        Assert.Equal(HttpStatusCode.OK, status);
        return (body.GetProperty("scope_mode").GetString()!,
            body.GetProperty("can_manage_scope").GetBoolean(),
            body.GetProperty("assignments").EnumerateArray().Select(e => e.GetGuid()).ToList());
    }

    public void Dispose() => Client.Dispose();
}

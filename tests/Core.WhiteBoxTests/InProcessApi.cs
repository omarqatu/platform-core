using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Core.WhiteBoxTests;

/// <summary>
/// Api hosted in-process — its real Program and pipeline — as it is deployed: with its three connection strings
/// and never migrator's. CI gives the test process migrator's connection string through the environment, and
/// Api's own T0 guard refuses to start if it sees it; so the variable is withheld while a host is built and
/// used, and put back afterwards. The fixture read its configuration before any test ran, and the white-box
/// tests run one at a time (one collection), so nothing else reads the environment meanwhile.
/// As a browser on the interface would, its clients send Origin (<see cref="Origin"/>, the host's one allowed origin)
/// and X-Requested-With: platform-web on every request (Api's CsrfProtection) — the harness's configuration, which
/// no test's expectation depends on.
/// </summary>
public static class InProcessApi
{
    private const string MigratorVariable = "ConnectionStrings__migrator";

    /// <summary>The origin the clients send, and the one Security:AllowedOrigins of every host built here.</summary>
    public const string Origin = "https://localhost";

    public static WebApplicationFactory<Program> Create(string? environment = null, params (string Key, string Value)[] settings) =>
        new HarnessFactory().WithWebHostBuilder(web =>
        {
            if (environment is not null)
                web.UseEnvironment(environment);
            foreach (var role in new[] { "app_user", "authenticator", "provisioner" })
                web.UseSetting($"ConnectionStrings:{role}", WhiteBoxFixture.ConnectionString(role));
            web.UseSetting("Security:AllowedOrigins:0", Origin);
            foreach (var (key, value) in settings)
                web.UseSetting(key, value);
        });

    private sealed class HarnessFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureClient(HttpClient client)
        {
            base.ConfigureClient(client);
            client.DefaultRequestHeaders.Add("Origin", Origin);
            client.DefaultRequestHeaders.Add("X-Requested-With", "platform-web");
        }
    }

    public static async Task WithoutMigratorVariableAsync(Func<Task> body)
    {
        var migrator = Environment.GetEnvironmentVariable(MigratorVariable);
        Environment.SetEnvironmentVariable(MigratorVariable, null);
        try
        {
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(MigratorVariable, migrator);
        }
    }
}

using System.Text.Json;
using Api;
using Api.Endpoints;
using Core.Data;
using Core.Http;
using Core.Identity;
using Core.Provisioning;
using Microsoft.AspNetCore.Authentication.Cookies;
using Modules.Subscriptions;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Api holds exactly three connection strings (PROOF_SPEC T0): app_user,
// authenticator, provisioner. migrator lives only in the migration tool and
// job_runner only in the background-job worker (spec item k).
string[] allowedConnections = ["app_user", "authenticator", "provisioner"];

var configured = builder.Configuration.GetSection("ConnectionStrings").GetChildren()
    .Select(c => c.Key)
    .ToArray();

var foreign = configured.Except(allowedConnections, StringComparer.OrdinalIgnoreCase).ToArray();
if (foreign.Length > 0)
    throw new InvalidOperationException(
        $"Api must not hold these connection strings: {string.Join(", ", foreign)}.");

foreach (var name in allowedConnections)
{
    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(name)))
        throw new InvalidOperationException($"ConnectionStrings:{name} is not configured.");
}

// The session cookie is Secure unless explicitly relaxed — and relaxing it is allowed only in an environment
// named Development or CI (a plain-HTTP test run). Anywhere else, Api refuses to start: the same pattern as
// the connection-string guard above (PROOF_SPEC T3, decision 33 as amended by the project owner).
string[] plainHttpEnvironments = ["Development", "CI"];
var requireHttps = builder.Configuration.GetValue("Session:RequireHttps", true);
if (!requireHttps && !plainHttpEnvironments.Contains(builder.Environment.EnvironmentName, StringComparer.Ordinal))
    throw new InvalidOperationException(
        $"Session:RequireHttps=false is allowed only in {string.Join(" or ", plainHttpEnvironments)}; " +
        $"this environment is '{builder.Environment.EnvironmentName}'.");

builder.Services.AddCoreDataAccess(builder.Configuration.GetConnectionString("app_user")!);
// The subscriptions module (PROOF_SPEC T5): its own context, on the same app_user data source and transaction layer.
builder.Services.AddDbContext<SubscriptionsDbContext>((services, options) =>
    options.UseCoreDataAccess(services.GetRequiredService<NpgsqlDataSource>()));

// The authenticator path (4.3-a/1): its own data source and role, never registered as the app_user one.
var authenticatorDataSource = CoreDataAccess.CreateDataSource(builder.Configuration.GetConnectionString("authenticator")!);
builder.Services.AddDbContext<AuthenticatorDbContext>(options => options.UseCoreDataAccess(authenticatorDataSource));

// The provisioner's two paths (4.4/2): its own data source and role — never SET ROLE on an app_user connection.
var provisionerDataSource = CoreDataAccess.CreateDataSource(builder.Configuration.GetConnectionString("provisioner")!);
builder.Services.AddDbContext<ProvisionerDbContext>(options => options.UseCoreDataAccess(provisionerDataSource));

// The session: an HTTP-only cookie carrying user_id and the active tenant only (PROOF_SPEC T3).
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISessionContextAccessor, CookieSessionAccessor>();
builder.Services.AddAuthentication(SessionCookie.Scheme)
    .AddCookie(SessionCookie.Scheme, options =>
    {
        options.Cookie.Name = "session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        // Secure unless explicitly relaxed for a plain-HTTP test run (guarded above).
        options.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<ErrorResponses>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UnitOfWorkMiddleware>();

app.MapAuthEndpoints();
app.MapTenantEndpoints();
app.MapScopeEndpoints();
app.MapMemberEndpoints();
app.MapAuditEndpoints();
app.MapSubscriptionEndpoints();
app.MapUnifiedSubscriptions();
app.MapSubscriptionScreen();
app.MapAcceptance();
// Bootstrap: registered in Development and CI only; in any other environment the route does not exist (T4.17).
app.MapBootstrap(app.Environment.EnvironmentName);

app.Lifetime.ApplicationStopped.Register(authenticatorDataSource.Dispose);
app.Lifetime.ApplicationStopped.Register(provisionerDataSource.Dispose);
app.Run();

public partial class Program;

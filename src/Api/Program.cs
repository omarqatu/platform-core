using System.Text.Json;
using Api;
using Api.Endpoints;
using Core.Data;
using Core.Http;
using Core.Identity;
using Core.Notifications;
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

// Every unsafe request needs an allowed Origin and X-Requested-With (CsrfProtection). None configured: Api refuses
// to start, rather than refuse every write.
var allowedOrigins = CsrfProtection.AllowedOrigins(builder.Configuration);

// Behind a reverse proxy, the client's address comes from the proxies configured here and no one else (ReverseProxy).
// A malformed entry: Api refuses to start.
var trustedProxies = ReverseProxy.TrustedProxies(builder.Configuration);

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

// The attempt limiter (Amendment 01 §5.2, OPEN_ITEMS 16): every number is configuration, and every one is a
// marked assumption until the project owner settles it at launch.
builder.Services.Configure<LoginLimits>(builder.Configuration.GetSection("Identity:LoginLimits"));

// Its delay sequence is one comma-separated value (OPEN_ITEMS 91), checked here so a bad one stops Api rather than
// the first failed login: a list in the old shape (a JSON array, or __0-style variables) would bind to nothing and
// silently become the default, and a malformed value is refused rather than skipped — fail-closed, both.
var delaySequence = builder.Configuration.GetSection("Identity:LoginLimits:DelaySecondsByFailure");
if (delaySequence.GetChildren().Any())
    throw new InvalidOperationException(
        "Identity:LoginLimits:DelaySecondsByFailure is a list; it is one comma-separated value, e.g. \"1,2,4,8,15\".");
LoginLimits.ParseDelaySeconds(delaySequence.Value);

// POST /auth/password/forgot (Amendment 01 §5.1): the token's life, and the floor that keeps the answer's
// timing from saying whether the address has an account (OPEN_ITEMS 51). Marked assumptions, like the limiter's.
builder.Services.Configure<ForgotPasswordSettings>(builder.Configuration.GetSection("Identity:ForgotPassword"));

// Invitations (OPEN_ITEMS 60, 106): the origin the acceptance link is built on, and the two rate limits.
builder.Services.Configure<InvitationSettings>(builder.Configuration.GetSection("Invitations"));

// The origin has no default and Api refuses to start without a valid one — the guard `Ops create-tenant` has,
// for the same reason: a template that falls back to a bare path sends a link nobody can click, silently, and
// the only person who finds out is the invitee who cannot join. (The recovery link still works that way:
// OPEN_ITEMS 105.) Checked here so a misconfigured deployment stops at boot, not at the first invitation.
var invitationSettings = builder.Configuration.GetSection("Invitations").Get<InvitationSettings>() ?? new InvitationSettings();
if (!invitationSettings.TryOrigin(out _))
    throw new InvalidOperationException(
        "Invitations:PublicOrigin must be set to the platform's origin, e.g. https://platform.example.com "
        + "(an absolute http or https URL with no path). The invitation e-mail's acceptance link is built on it.");

// The sender (Amendment 01 §5.5): one implementation per channel, email first (D3). The account comes from the
// platform's provider_accounts row and the secret from the settings key its credential_ref names — never from
// the database (§2.6). The dispatcher takes the provider round trip off the request path, which is what lets
// forgot answer in the same time either way.
builder.Services.Configure<EmailSenderOptions>(builder.Configuration.GetSection("Notifications:Email"));
builder.Services.Configure<PlatformProviderOptions>(builder.Configuration.GetSection("Notifications:ProviderAccounts"));
builder.Services.Configure<NotificationDispatchOptions>(builder.Configuration.GetSection("Notifications:Dispatch"));
builder.Services.AddSingleton<IPlatformProviderAccounts, PlatformProviderAccounts>();
builder.Services.AddSingleton<INotificationSender, EmailNotificationSender>();
builder.Services.AddSingleton<NotificationDispatch>();
builder.Services.AddSingleton<INotificationDispatch>(s => s.GetRequiredService<NotificationDispatch>());
builder.Services.AddHostedService<NotificationDispatcher>();

// The session: an HTTP-only cookie carrying user_id, the active tenant and the security stamp (PROOF_SPEC T3,
// Amendment 01 §2.1).
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

// The client's address first, so everything after it sees it; then CSRF on every path, before routing; then the
// interface for what no route matches (WebInterface).
app.UseClientAddress(trustedProxies);
app.UseCsrfProtection(allowedOrigins);
app.UseRouting();
app.UseWebInterface();
app.UseMiddleware<ErrorResponses>();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<UnitOfWorkMiddleware>();

// Every API route lives under /api, and only there (OPEN_ITEMS 37, decided by the project owner).
var api = app.MapGroup(WebInterface.ApiPrefix);
api.MapAuthEndpoints();
api.MapTenantEndpoints();
api.MapScopeEndpoints();
api.MapMemberEndpoints();
api.MapAuditEndpoints();
api.MapSubscriptionEndpoints();
api.MapUnifiedSubscriptions();
api.MapAcceptance();
// Bootstrap: registered in Development and CI only; in any other environment the route does not exist (T4.17).
api.MapBootstrap(app.Environment.EnvironmentName);

// The one route outside /api and the interface: the original T8 screen, part of a merged proof (PROOF_SPEC T8.2),
// kept at its path and never edited (WebInterface.ServerRenderedScreens).
app.MapSubscriptionScreen();

app.Lifetime.ApplicationStopped.Register(authenticatorDataSource.Dispose);
app.Lifetime.ApplicationStopped.Register(provisionerDataSource.Dispose);
app.Run();

public partial class Program;

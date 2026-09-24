using Core.Data;
using Core.Http;

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

builder.Services.AddCoreDataAccess(builder.Configuration.GetConnectionString("app_user")!);
builder.Services.AddSingleton<ISessionContextAccessor, NoSessionContextAccessor>();

var app = builder.Build();

app.UseMiddleware<UnitOfWorkMiddleware>();

app.Run();

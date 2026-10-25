using Core.Data;
using Core.Identity;
using Core.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Ops;

/// <summary>
/// The commands of the operations procedure (Amendment 01 §5.4) and the guard in front of them. Built: create-tenant,
/// in part (OPEN_ITEMS 89). Not built: mark-paid and suspend-unpaid — both need platform_subscriptions.
/// </summary>
public static class OpsCommand
{
    /// <summary>The only connection strings this process may hold (§5.4): any other refuses it, before anything runs.</summary>
    public static readonly IReadOnlyList<string> AllowedConnections = ["provisioner", "job_runner"];

    /// <summary>The role no connection string here may sign in as, whatever its key (§5.4: "the same guard as Api").</summary>
    private const string ForbiddenRole = "migrator";

    private const int MaxText = 256;

    public const string Usage = "Usage: Ops create-tenant --name <name> --owner-email <address>";

    public static async Task<int> RunAsync(string[] args, IConfiguration configuration, TextWriter stdout, TextWriter stderr,
        CancellationToken cancellationToken = default)
    {
        if (RefusedConnections(configuration) is { } refusal)
        {
            await stderr.WriteLineAsync(refusal);
            return 1;
        }

        if (args is not ["create-tenant", .. var options])
        {
            await stderr.WriteLineAsync(Usage);
            return 1;
        }
        return await CreateTenantAsync(options, configuration, stdout, stderr, cancellationToken);
    }

    /// <summary>
    /// The guard: a connection string under a key not allowed here, or one that signs in as the forbidden role under
    /// an allowed key. Null when the configuration may run.
    /// </summary>
    internal static string? RefusedConnections(IConfiguration configuration)
    {
        var configured = configuration.GetSection("ConnectionStrings").GetChildren().ToList();
        var foreign = configured.Select(c => c.Key).Except(AllowedConnections, StringComparer.OrdinalIgnoreCase).ToArray();
        if (foreign.Length > 0)
            return $"Ops must not hold these connection strings: {string.Join(", ", foreign)}.";
        foreach (var connection in configured)
        {
            if (string.IsNullOrWhiteSpace(connection.Value))
                continue;
            string? username;
            try
            {
                username = new NpgsqlConnectionStringBuilder(connection.Value).Username;
            }
            catch (ArgumentException)
            {
                return $"ConnectionStrings:{connection.Key} is not a connection string Ops can read.";
            }
            if (string.Equals(username, ForbiddenRole, StringComparison.OrdinalIgnoreCase))
                return $"ConnectionStrings:{connection.Key} signs in as a role Ops must never hold.";
        }
        return null;
    }

    private static async Task<int> CreateTenantAsync(string[] options, IConfiguration configuration, TextWriter stdout, TextWriter stderr,
        CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < options.Length; i += 2)
        {
            if (options[i] is not ("--name" or "--owner-email") || i + 1 >= options.Length || values.ContainsKey(options[i]))
            {
                // --plan is in §5.4's signature, and refused by name: plans do not exist yet (OPEN_ITEMS 89).
                await stderr.WriteLineAsync(options[i] == "--plan"
                    ? "--plan is not built: the plans and platform_subscriptions tables do not exist yet (OPEN_ITEMS 89)."
                    : Usage);
                return 1;
            }
            values[options[i]] = options[i + 1];
        }
        if (values.GetValueOrDefault("--name") is not { Length: > 0 and <= MaxText } name
            || values.GetValueOrDefault("--owner-email") is not { Length: > 0 and <= MaxText } ownerEmail
            // The owner invitation is sent to this address (item 35): one deliverable address, or nothing is written.
            || !EmailAddress.IsSendable(ownerEmail))
        {
            await stderr.WriteLineAsync(Usage);
            return 1;
        }

        // The link's origin is configuration, never guessed: a path alone would leave the operator to guess it.
        if (!TryPublicOrigin(configuration["Ops:PublicOrigin"], out var origin))
        {
            await stderr.WriteLineAsync("Ops:PublicOrigin must be set to the platform's origin, e.g. https://platform.example.com "
                + "(an absolute http or https URL with no path).");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("provisioner")))
        {
            await stderr.WriteLineAsync("ConnectionStrings:provisioner is not configured.");
            return 1;
        }

        await using var dataSource = CoreDataAccess.CreateDataSource(configuration.GetConnectionString("provisioner")!);
        var builder = new DbContextOptionsBuilder<ProvisionerDbContext>();
        builder.UseCoreDataAccess(dataSource);
        await using var db = new ProvisionerDbContext(builder.Options);
        var result = await CreateTenant.RunAsync(db, new CreateTenantRequest(name, ownerEmail), ct);

        await stdout.WriteLineAsync($"tenant_id: {result.TenantId:D}");
        await stdout.WriteLineAsync($"owner invitation for {ownerEmail}, single use, expires {result.ExpiresAt:yyyy-MM-dd HH:mm} UTC:");
        await stdout.WriteLineAsync(AcceptLink(origin, result.TenantId, result.Token));
        await stdout.WriteLineAsync("This link is shown once and stored nowhere. If it is lost, run create-tenant again.");
        return 0;
    }

    // The link's shape and the origin's rule live in Core (AcceptanceLink): the e-mail that POST
    // /members/invitations sends carries the same link, and two definitions of it would drift.
    internal static string AcceptLink(Uri origin, Guid tenantId, string token) => AcceptanceLink.For(origin, tenantId, token);

    internal static bool TryPublicOrigin(string? value, out Uri origin) => AcceptanceLink.TryParseOrigin(value, out origin);
}

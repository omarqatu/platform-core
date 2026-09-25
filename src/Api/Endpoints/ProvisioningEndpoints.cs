using Core.Data;
using Core.Http;
using Core.Provisioning;

namespace Api.Endpoints;

public sealed record BootstrapBody(string? TenantName, string? FullName, string? Email, string? Username, string? Password);

public sealed record AcceptBody(Guid? TenantId, string? Token, string? Email, string? FullName, string? Username, string? Password);

/// <summary>
/// The provisioner's two paths, and no others (4.4/3): bootstrap and invitation acceptance. Each runs its own
/// provisioner transaction (ProvisionerUnitOfWork), never the request's app_user one.
/// </summary>
public static class ProvisioningEndpoints
{
    /// <summary>
    /// Bootstrap is registered in these environments only (3.10, PROOF_SPEC T4.17): in any other — Production and
    /// Staging among them — the route does not exist. Public self-registration is out of the proof's scope.
    /// </summary>
    public static readonly IReadOnlyList<string> BootstrapEnvironments = ["Development", "CI"];

    public static bool IsBootstrapEnvironment(string environment) => BootstrapEnvironments.Contains(environment, StringComparer.Ordinal);

    private const int MaxText = 256;
    private const int MaxPassword = 1024;

    /// <summary>
    /// Registers POST /provision/tenants when the guard admits the environment; returns whether it did. The guard is
    /// a parameter so the white-box test can show its own detector failing with the guard removed (T4.17).
    /// </summary>
    public static bool MapBootstrap(this IEndpointRouteBuilder app, string environment, Func<string, bool>? guard = null)
    {
        if (!(guard ?? IsBootstrapEnvironment)(environment))
            return false;

        app.MapPost("/provision/tenants", async (BootstrapBody body, ProvisionerDbContext db, HttpContext http, CancellationToken ct) =>
        {
            if (body is not
                {
                    TenantName: { Length: > 0 and <= MaxText } tenantName, FullName: { Length: > 0 and <= MaxText } fullName,
                    Email: { Length: > 0 and <= MaxText } email, Username: { Length: > 0 and <= MaxText } username,
                    Password: { Length: > 0 and <= MaxPassword } password,
                })
                return InvalidRequest();

            db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();
            var result = await Bootstrap.RunAsync(db, new BootstrapRequest(tenantName, fullName, email, username, password), ct);
            return Results.Json(new { tenant_id = result.TenantId, user_id = result.UserId, membership_id = result.MembershipId },
                statusCode: StatusCodes.Status201Created);
        }).WithMetadata(new OwnUnitsOfWorkAttribute());
        return true;
    }

    /// <summary>
    /// Accepting an invitation (4.5/3): the target tenant id alongside the token (T4.11). An authenticated caller
    /// accepts as their own account; a caller with no session registers the account in the same transaction.
    /// </summary>
    public static void MapAcceptance(this IEndpointRouteBuilder app)
    {
        app.MapPost("/invitations/accept", async (AcceptBody body, ProvisionerDbContext db, ISessionContextAccessor session,
            HttpContext http, CancellationToken ct) =>
        {
            if (body is not { TenantId: { } tenantId, Token: { Length: > 0 and <= MaxText } token })
                return InvalidRequest();

            var user = session.Current.UserId;
            NewAccount? account = null;
            if (user is null)
            {
                if (body is not
                    {
                        Email: { Length: > 0 and <= MaxText } email, FullName: { Length: > 0 and <= MaxText } fullName,
                        Username: { Length: > 0 and <= MaxText } username, Password: { Length: > 0 and <= MaxPassword } password,
                    })
                    return InvalidRequest();
                account = new NewAccount(email, fullName, username, password);
            }

            db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();
            await Acceptance.RunAsync(db, new AcceptRequest(tenantId, token, account), user, ct);
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute());
    }

    private static IResult InvalidRequest() =>
        Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);
}

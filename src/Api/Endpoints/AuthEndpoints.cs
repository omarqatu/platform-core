using Core;
using Core.Data;
using Core.Http;
using Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record LoginRequest(string? Username, string? Password);

public static class AuthEndpoints
{
    private const int MaxUsername = 256;
    private const int MaxPassword = 1024;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // The login path (4.3): authenticator resolves the credential and logs the attempt with no tenant
        // context; then, as app_user with app.user_id alone, last_login_at — a critical write (3.5/5, 4.3 (6)).
        // The cookie carries the user only; a tenant is chosen next.
        app.MapPost("/auth/login", async (LoginRequest body, HttpContext http, AuthenticatorDbContext authenticator,
            CoreDbContext db, CancellationToken ct) =>
        {
            if (body is not { Username: { Length: > 0 and <= MaxUsername } username, Password: { Length: <= MaxPassword } password })
                return Results.Json(new { error = "invalid_request" }, statusCode: StatusCodes.Status400BadRequest);

            var userId = await Authenticator.AuthenticateAsync(
                authenticator, username, password, http.Connection.RemoteIpAddress?.ToString(), ct);
            // One response for every failure — a missing username and a wrong password alike (T3.5).
            if (userId is not { } user)
                return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

            await UnitOfWork.RunAsync(db, new SessionContext(user, null), (c, token) =>
                CriticalWrite.ExpectRowsAsync(
                    c.Users.Where(u => u.Id == user)
                        .ExecuteUpdateAsync(set => set.SetProperty(u => u.LastLoginAt, DateTime.UtcNow), token),
                    1, "users.last_login_at"), ct);

            await http.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(user, null));
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute());
    }
}

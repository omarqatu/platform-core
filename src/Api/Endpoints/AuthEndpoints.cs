using Core;
using Core.Data;
using Core.Http;
using Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record LoginRequest(string? Username, string? Password);

/// <summary>The current session as the interface shows it: the user, and the active tenant if it can still be entered.</summary>
public sealed record MeResponse(string Username, MeTenant? ActiveTenant);

public sealed record MeTenant(Guid TenantId, string Name);

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
                return Results.Json(new { error = ApiErrorCodes.InvalidRequest }, statusCode: StatusCodes.Status400BadRequest);

            var userId = await Authenticator.AuthenticateAsync(
                authenticator, username, password, http.Connection.RemoteIpAddress?.ToString(), ct);
            // One response for every failure — a missing username and a wrong password alike (T3.5).
            if (userId is not { } user)
                return Results.Json(new { error = ApiErrorCodes.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            // A tracked update of the actor's own row: exempt from automatic auditing (7, self-service identity).
            await UnitOfWork.RunAsync(db, new SessionContext(user, null), async (c, token) =>
            {
                var row = CriticalWrite.Require(await c.Users.SingleOrDefaultAsync(u => u.Id == user, token), "users.last_login_at");
                row.LastLoginAt = DateTime.UtcNow;
                await CriticalWrite.SaveAsync(c, "users.last_login_at", token);
            }, ct);

            await http.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(user, null));
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute());

        // Logging out: the cookie is expired in the browser. No database access, and no session required — an expired
        // one logs out too. The cookie is self-contained: a copy kept elsewhere stays valid until it expires
        // (OPEN_ITEMS 36). An unsafe request, so CsrfProtection applies.
        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(SessionCookie.Scheme);
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute());

        // The current session, for the interface's identity bar: app.user_id alone, like the tenant-selection path
        // (4.3-b, T3.7) — the user's own row (user_self_read), and the cookie's tenant only if it is among the user's
        // own active memberships in an active tenant (membership_self, tenant_visible_to_member). Otherwise
        // active_tenant is null, and nothing is said about why. No role, scope or permission.
        app.MapGet("/me", async (CoreDbContext db, ISessionContextAccessor session, CancellationToken ct) =>
        {
            var user = session.Current.UserId!.Value;
            var username = await db.Users.Where(u => u.Id == user).Select(u => u.Username).SingleAsync(ct);
            MeTenant? active = null;
            if (session.Current.TenantId is { } tenant)
                active = await (
                    from m in db.Memberships
                    where m.UserId == user && m.TenantId == tenant && m.Status == "active"
                    join t in db.Tenants on m.TenantId equals t.Id
                    where t.Status == "active"
                    select new MeTenant(t.Id, t.Name)).SingleOrDefaultAsync(ct);
            return Results.Ok(new MeResponse(username, active));
        }).RequireAuthorization().WithMetadata(new WithoutActiveTenantAttribute());
    }
}

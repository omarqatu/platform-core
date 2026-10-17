using Core;
using Core.Data;
using Core.Http;
using Core.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public sealed record LoginRequest(string? Username, string? Password);

/// <summary>Amendment 01 §5.1: the recovery token, and the password it sets.</summary>
public sealed record ResetPasswordRequest(string? Token, string? NewPassword);

/// <summary>Amendment 01 §5.1: the current password, and the one that replaces it.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

/// <summary>The current session as the interface shows it: the user, and the active tenant if it can still be entered.</summary>
public sealed record MeResponse(string Username, MeTenant? ActiveTenant);

public sealed record MeTenant(Guid TenantId, string Name);

public static class AuthEndpoints
{
    private const int MaxUsername = 256;
    private const int MaxPassword = 1024;
    private const int MaxToken = 256;

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // The login path (4.3): authenticator resolves the credential and logs the attempt with no tenant
        // context; then, as app_user with app.user_id alone, last_login_at — a critical write (3.5/5, 4.3 (6)).
        // The cookie carries the user only; a tenant is chosen next.
        app.MapPost("/auth/login", async (LoginRequest body, HttpContext http, AuthenticatorDbContext authenticator,
            CoreDbContext db, IOptions<LoginLimits> limits, CancellationToken ct) =>
        {
            if (body is not { Username: { Length: > 0 and <= MaxUsername } username, Password: { Length: <= MaxPassword } password })
                return Results.Json(new { error = ApiErrorCodes.InvalidRequest }, statusCode: StatusCodes.Status400BadRequest);

            var attempt = await Authenticator.AuthenticateAsync(
                authenticator, username, password, http.Connection.RemoteIpAddress?.ToString(), limits.Value, ct);
            // The ip ceiling of Amendment 01 §5.2: refused before the password was read. It depends on the
            // address alone, so it says nothing about which usernames exist.
            if (attempt.Outcome is AuthOutcome.RefusedTooManyAttempts)
                return Results.Json(new { error = ApiErrorCodes.TooManyAttempts }, statusCode: StatusCodes.Status429TooManyRequests);
            // One response for every failure — a missing username and a wrong password alike (T3.5).
            if (attempt.UserId is not { } user)
                return Results.Json(new { error = ApiErrorCodes.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            // A tracked update of the actor's own row: exempt from automatic auditing (7, self-service identity).
            // The same read carries the security stamp into the cookie (Amendment 01 §2.1).
            var stamp = await UnitOfWork.RunAsync(db, new SessionContext(user, null), async (c, token) =>
            {
                var row = CriticalWrite.Require(await c.Users.SingleOrDefaultAsync(u => u.Id == user, token), "users.last_login_at");
                row.LastLoginAt = DateTime.UtcNow;
                await CriticalWrite.SaveAsync(c, "users.last_login_at", token);
                return row.SecurityStamp;
            }, ct);

            await http.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(user, null, stamp));
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute());

        // Logging out: the cookie is expired in the browser. No database access, and no session required — an expired
        // one logs out too, and a session the stamp has already ended with it (WithoutSessionStamp), or a stale
        // cookie could never be cleared. The cookie is self-contained: a copy kept elsewhere stays valid until it expires
        // (OPEN_ITEMS 36). An unsafe request, so CsrfProtection applies.
        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(SessionCookie.Scheme);
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute(), new WithoutSessionStampAttribute());

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

        // Setting a password from a recovery token (Amendment 01 §5.1), two units of work and no session:
        // UoW-1, as authenticator, verifies the token and consumes it; UoW-2, as app_user in the context of the
        // token's owner — the named exception to §4.3-b (§0): app.user_id comes from the consumed row, never
        // from the request — writes the new hash and a new security stamp. No cookie is issued: every session of
        // that user, this browser's included, ends on its next request (§2.1).
        app.MapPost("/auth/password/reset", async (ResetPasswordRequest body, AuthenticatorDbContext authenticator,
            CoreDbContext db, CancellationToken ct) =>
        {
            if (body is not { Token: { Length: > 0 and <= MaxToken } token,
                              NewPassword: { Length: > 0 and <= MaxPassword } newPassword })
                return Results.Json(new { error = ApiErrorCodes.InvalidRequest }, statusCode: StatusCodes.Status400BadRequest);

            var owner = await PasswordPaths.ConsumeResetTokenAsync(authenticator, token, ct);
            await PasswordPaths.ApplyNewPasswordAsync(db, owner, newPassword, ct);
            return Results.NoContent();
        }).WithMetadata(new OwnUnitsOfWorkAttribute());

        // Changing one's own password (Amendment 01 §5.1), two units of work with a session: UoW-1, as
        // authenticator, verifies the current password for the session's user and writes its auth_attempts row;
        // UoW-2, as app_user, writes the new hash and a new stamp. The cookie is re-issued with that stamp, so
        // this session survives and every other session of the same user ends.
        app.MapPut("/me/password", async (ChangePasswordRequest body, HttpContext http,
            AuthenticatorDbContext authenticator, CoreDbContext db, ISessionContextAccessor session, CancellationToken ct) =>
        {
            if (body is not { CurrentPassword: { Length: > 0 and <= MaxPassword } current,
                              NewPassword: { Length: > 0 and <= MaxPassword } newPassword })
                return Results.Json(new { error = ApiErrorCodes.InvalidRequest }, statusCode: StatusCodes.Status400BadRequest);

            var user = session.Current.UserId!.Value;
            if (!await PasswordPaths.VerifyCurrentPasswordAsync(
                    authenticator, user, current, http.Connection.RemoteIpAddress?.ToString(), ct))
                return Results.Json(new { error = ApiErrorCodes.InvalidCredentials }, statusCode: StatusCodes.Status401Unauthorized);

            var stamp = await PasswordPaths.ApplyNewPasswordAsync(db, user, newPassword, ct);
            await http.SignInAsync(SessionCookie.Scheme, SessionCookie.Principal(user, session.Current.TenantId, stamp));
            return Results.NoContent();
        }).RequireAuthorization().WithMetadata(new OwnUnitsOfWorkAttribute());
    }
}

using System.Security.Claims;
using Core.Data;
using Microsoft.AspNetCore.Http;

namespace Core.Http;

/// <summary>
/// The session cookie's whole content (PROOF_SPEC T3, Amendment 01 §2.1): the user's id, the active tenant's id,
/// and the user's security stamp — no scope, no permission, no role (3.5/6: the second axis is resolved per
/// transaction, never stored in the session). The stamp is compared with the users row on every request
/// (SessionStamp), so a password change ends every session.
/// </summary>
public static class SessionCookie
{
    public const string Scheme = "session";
    public const string UserId = "user_id";
    public const string TenantId = "tenant_id";
    public const string SecurityStamp = "security_stamp";

    public static ClaimsPrincipal Principal(Guid userId, Guid? tenantId, Guid securityStamp)
    {
        List<Claim> claims = [new(UserId, userId.ToString("D")), new(SecurityStamp, securityStamp.ToString("D"))];
        if (tenantId is { } tenant)
            claims.Add(new Claim(TenantId, tenant.ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, UserId, null));
    }

    /// <summary>The stamp the cookie carries, or null when it carries none (a cookie minted before §2.1).</summary>
    public static Guid? Stamp(ClaimsPrincipal? principal) =>
        principal?.Identity is { IsAuthenticated: true } && Guid.TryParse(principal.FindFirstValue(SecurityStamp), out var stamp)
            ? stamp
            : null;

    public static SessionContext Read(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not { IsAuthenticated: true } || !Guid.TryParse(principal.FindFirstValue(UserId), out var user))
            return SessionContext.None;
        return new SessionContext(user, Guid.TryParse(principal.FindFirstValue(TenantId), out var tenant) ? tenant : null);
    }
}

/// <summary>The request's session, read from the authenticated cookie alone.</summary>
public sealed class CookieSessionAccessor(IHttpContextAccessor http) : ISessionContextAccessor
{
    public SessionContext Current => SessionCookie.Read(http.HttpContext?.User);

    public Guid? SecurityStamp => SessionCookie.Stamp(http.HttpContext?.User);
}

/// <summary>
/// Endpoint metadata: the tenant-selection path (4.3-b) — the request's transaction carries app.user_id alone,
/// whatever tenant the cookie holds, so a member can list and switch tenants even when the active one can no
/// longer be entered.
/// </summary>
public sealed class WithoutActiveTenantAttribute : Attribute;

/// <summary>
/// Endpoint metadata: the session stamp is not compared for this endpoint (Amendment 01 §2.1). The two endpoints
/// that carry it touch no data at all — logging out, and leaving the active tenant — and both only narrow the
/// session. Logging out must work with a session the stamp has already ended, or a stale cookie could never be
/// cleared from the browser; and a stamp carried over by either is compared on the next request that reads or
/// writes anything.
/// </summary>
public sealed class WithoutSessionStampAttribute : Attribute;

/// <summary>
/// Endpoint metadata: the endpoint runs its own units of work — login (an authenticator transaction, then an
/// app_user one) and tenant selection (a transaction for the requested tenant before the cookie holds it).
/// Every database access still goes through UnitOfWork; the interceptor enforces it (3.5).
/// </summary>
public sealed class OwnUnitsOfWorkAttribute : Attribute;

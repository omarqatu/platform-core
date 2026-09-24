using System.Security.Claims;
using Core.Data;
using Microsoft.AspNetCore.Http;

namespace Core.Http;

/// <summary>
/// The session cookie's whole content (PROOF_SPEC T3): the user's id and the active tenant's id — no scope, no
/// permission, no role (3.5/6: the second axis is resolved per transaction, never stored in the session).
/// </summary>
public static class SessionCookie
{
    public const string Scheme = "session";
    public const string UserId = "user_id";
    public const string TenantId = "tenant_id";

    public static ClaimsPrincipal Principal(Guid userId, Guid? tenantId)
    {
        List<Claim> claims = [new(UserId, userId.ToString("D"))];
        if (tenantId is { } tenant)
            claims.Add(new Claim(TenantId, tenant.ToString("D")));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, UserId, null));
    }

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
}

/// <summary>
/// Endpoint metadata: the tenant-selection path (4.3-b) — the request's transaction carries app.user_id alone,
/// whatever tenant the cookie holds, so a member can list and switch tenants even when the active one can no
/// longer be entered.
/// </summary>
public sealed class WithoutActiveTenantAttribute : Attribute;

/// <summary>
/// Endpoint metadata: the endpoint runs its own units of work — login (an authenticator transaction, then an
/// app_user one) and tenant selection (a transaction for the requested tenant before the cookie holds it).
/// Every database access still goes through UnitOfWork; the interceptor enforces it (3.5).
/// </summary>
public sealed class OwnUnitsOfWorkAttribute : Attribute;

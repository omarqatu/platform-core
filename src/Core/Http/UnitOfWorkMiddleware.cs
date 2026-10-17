using Core.Data;
using Microsoft.AspNetCore.Http;

namespace Core.Http;

/// <summary>
/// Every request → a single explicit transaction (3.5/1). The transaction
/// commits when the pipeline completes, and rolls back if it throws.
/// Since Amendment 01 §2.1 it also compares the cookie's security stamp with the user's row, on every request
/// that carries a session: inside the request's own transaction where there is one, and in a short read-only
/// transaction of its own where the endpoint runs its own units of work.
/// </summary>
public sealed class UnitOfWorkMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, CoreDbContext db, ISessionContextAccessor session)
    {
        // Recorded on this request's audit entries (7).
        db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();
        var metadata = http.GetEndpoint()?.Metadata;
        var current = session.Current;
        // No session, or an endpoint that must work with one the stamp has ended (logout): nothing to compare.
        var stampUser = metadata?.GetMetadata<WithoutSessionStampAttribute>() is null ? current.UserId : null;

        if (metadata?.GetMetadata<OwnUnitsOfWorkAttribute>() is not null)
        {
            if (stampUser is { } own)
                await UnitOfWork.RunReadOnlyAsync(db, new SessionContext(own, null),
                    (c, ct) => SessionStamp.VerifyAsync(c, own, session.SecurityStamp, ct), http.RequestAborted);
            await next(http);
            return;
        }

        if (metadata?.GetMetadata<WithoutActiveTenantAttribute>() is not null)
            current = current with { TenantId = null };
        await UnitOfWork.RunAsync(db, current, async (c, ct) =>
        {
            if (stampUser is { } user)
                await SessionStamp.VerifyAsync(c, user, session.SecurityStamp, ct);
            await next(http);
        }, http.RequestAborted);
    }
}

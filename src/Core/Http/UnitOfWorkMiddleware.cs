using Core.Data;
using Microsoft.AspNetCore.Http;

namespace Core.Http;

/// <summary>
/// Every request → a single explicit transaction (3.5/1). The transaction
/// commits when the pipeline completes, and rolls back if it throws.
/// </summary>
public sealed class UnitOfWorkMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http, CoreDbContext db, ISessionContextAccessor session)
    {
        // Recorded on this request's audit entries (7).
        db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();
        var metadata = http.GetEndpoint()?.Metadata;
        if (metadata?.GetMetadata<OwnUnitsOfWorkAttribute>() is not null)
            return next(http);

        var current = session.Current;
        if (metadata?.GetMetadata<WithoutActiveTenantAttribute>() is not null)
            current = current with { TenantId = null };
        return UnitOfWork.RunAsync(db, current, (_, _) => next(http), http.RequestAborted);
    }
}

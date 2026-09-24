using Core.Data;
using Microsoft.AspNetCore.Http;

namespace Core.Http;

/// <summary>
/// Every request → a single explicit transaction (3.5/1). The transaction
/// commits when the pipeline completes, and rolls back if it throws.
/// </summary>
public sealed class UnitOfWorkMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http, CoreDbContext db, ISessionContextAccessor session) =>
        UnitOfWork.RunAsync(db, session.Current, (_, _) => next(http), http.RequestAborted);
}

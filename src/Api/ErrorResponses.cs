using Core.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api;

/// <summary>
/// Maps the loud upper layer's exceptions to responses. The transaction has already rolled back by the time an
/// exception reaches here (UnitOfWork). Anything not listed propagates unchanged.
/// </summary>
public sealed class ErrorResponses(RequestDelegate next, ILogger<ErrorResponses> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await next(http);
        }
        catch (Exception error) when (Map(error) is { } mapped && !http.Response.HasStarted)
        {
            if (mapped.Status >= 500)
                logger.LogError(error, "{Code}", mapped.Code);
            http.Response.Clear();
            await Results.Json(new { error = mapped.Code }, statusCode: mapped.Status).ExecuteAsync(http);
        }
    }

    private static (int Status, string Code)? Map(Exception error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            switch (e)
            {
                // Rule 7 (3.5/7): a membership without a scope row is invalid data — loud, never a default.
                case MissingMembershipScopeException:
                    return (StatusCodes.Status500InternalServerError, "membership_scope_missing");
                case NoActiveMembershipException:
                    return (StatusCodes.Status403Forbidden, "not_a_member");
                // The rows-affected guard (3.5/5): the row is not writable under this context.
                case CriticalWriteException:
                    return (StatusCodes.Status403Forbidden, "not_permitted");
                case PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege }:
                    return (StatusCodes.Status403Forbidden, "not_permitted");
                case PostgresException { SqlState: PostgresErrorCodes.CheckViolation }:
                    return (StatusCodes.Status400BadRequest, "invalid_value");
                case PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }:
                    return (StatusCodes.Status409Conflict, "conflict");
            }
        }
        return null;
    }
}

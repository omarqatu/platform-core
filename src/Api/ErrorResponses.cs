using Core;
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
                    return (StatusCodes.Status500InternalServerError, ApiErrorCodes.MembershipScopeMissing);
                case NoActiveMembershipException:
                    return (StatusCodes.Status403Forbidden, ApiErrorCodes.NotAMember);
                // The provisioner paths (4.4): an acceptance refused before any write; bootstrap without templates (T4.9).
                case Core.Provisioning.InvitationRefusedException refused:
                    return (refused.Code switch
                    {
                        ApiErrorCodes.InvalidRequest => StatusCodes.Status400BadRequest,
                        ApiErrorCodes.AccountExists or ApiErrorCodes.UsernameTaken => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status403Forbidden,
                    }, refused.Code);
                // Items a and g (3.10): the change would leave no active owner / no active 'all' membership.
                case Endpoints.LastMemberException last:
                    return (StatusCodes.Status409Conflict, last.Code);
                case Core.Provisioning.RoleTemplatesUnavailableException:
                    return (StatusCodes.Status500InternalServerError, ApiErrorCodes.RoleTemplatesUnavailable);
                // The application layer's explicit permission check (5), before any write.
                case NotPermittedException:
                    return (StatusCodes.Status403Forbidden, ApiErrorCodes.NotPermitted);
                // The rows-affected guard (3.5/5): the row is not writable under this context.
                case CriticalWriteException:
                    return (StatusCodes.Status403Forbidden, ApiErrorCodes.NotPermitted);
                case PostgresException { SqlState: PostgresErrorCodes.InsufficientPrivilege }:
                    return (StatusCodes.Status403Forbidden, ApiErrorCodes.NotPermitted);
                case PostgresException { SqlState: PostgresErrorCodes.CheckViolation }:
                    return (StatusCodes.Status400BadRequest, ApiErrorCodes.InvalidValue);
                // A reference to another tenant's row, or to none: the composite FK (3.3) — never a leak of which.
                case PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation }:
                    return (StatusCodes.Status400BadRequest, ApiErrorCodes.InvalidReference);
                case PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }:
                    return (StatusCodes.Status409Conflict, ApiErrorCodes.Conflict);
            }
        }
        return null;
    }
}

using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

public sealed record ClientItem(Guid Id, string Name);
public sealed record SubscriptionItem(Guid Id, Guid ClientId, string ServiceName, DateOnly EndsOn);
public sealed record CreateSubscriptionRequest(Guid? ClientId, string? ServiceName, DateOnly? EndsOn);

/// <summary>
/// The module's API (PROOF_SPEC T5): the client list, the subscription list, creating a subscription. Each runs its
/// own unit of work on the module's context — resolution first (3.5/6) — and requires its permission (5) before
/// anything else. Both lists carry the scope declaration (3.5). The second axis itself is RLS beneath: no query
/// here filters by scope.
/// </summary>
public static class SubscriptionEndpoints
{
    public const string Read = "subscriptions.read";
    public const string Write = "subscriptions.write";
    private const int MaxServiceName = 256;

    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var module = app.MapGroup("/subscriptions").RequireAuthorization().WithMetadata(new OwnUnitsOfWorkAttribute());

        module.MapGet("/clients", (int? offset, int? limit, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                var scope = await Permissions.RequireAsync(c, Read, t);
                return Results.Ok(await ScopedList.PageAsync(scope,
                    c.Clients.OrderBy(x => x.Id).Select(x => new ClientItem(x.Id, x.Name)), offset, limit, t));
            }, ct));

        module.MapGet("", (int? offset, int? limit, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                var scope = await Permissions.RequireAsync(c, Read, t);
                return Results.Ok(await ScopedList.PageAsync(scope,
                    c.Subscriptions.OrderBy(x => x.Id).Select(x => new SubscriptionItem(x.Id, x.ScopeRefId, x.ServiceName, x.EndsOn)),
                    offset, limit, t));
            }, ct));

        // Creating a subscription: its client must be one the caller sees — client_scope's WITH CHECK refuses any
        // other (42501), and the composite FK any client of another tenant (23503).
        module.MapPost("", (CreateSubscriptionRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Write, t);
                if (body is not { ClientId: { } client, ServiceName: { Length: > 0 and <= MaxServiceName } name, EndsOn: { } endsOn })
                    return Results.Json(new { error = "invalid_value" }, statusCode: StatusCodes.Status400BadRequest);
                var subscription = new Subscription
                {
                    Id = Guid.CreateVersion7(), TenantId = session.Current.TenantId!.Value, ScopeRefId = client, ServiceName = name,
                    EndsOn = endsOn, CreatedAt = DateTime.UtcNow,
                };
                c.Subscriptions.Add(subscription);
                await CriticalWrite.SaveAsync(c, "subscriptions insert", t);
                return Results.Json(new { id = subscription.Id }, statusCode: StatusCodes.Status201Created);
            }, ct));
    }

    private static Task<IResult> InTenantAsync(HttpContext http, SubscriptionsDbContext db, ISessionContextAccessor session,
        Func<SubscriptionsDbContext, CancellationToken, Task<IResult>> work, CancellationToken ct)
    {
        db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();   // recorded on the audit entries (7)
        if (session.Current.TenantId is null)
            return Task.FromResult(Results.Json(new { error = "no_active_tenant" }, statusCode: StatusCodes.Status409Conflict));
        return UnitOfWork.RunAsync(db, session.Current, work, ct);
    }
}

using System.Text.RegularExpressions;
using Core;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

public sealed record ClientItem(Guid Id, string Name);
public sealed record SubscriptionItem(Guid Id, Guid ClientId, string ServiceName, DateOnly EndsOn, Guid CategoryId);
public sealed record SubscriptionDetail(Guid Id, Guid ClientId, string ServiceName, DateOnly EndsOn, string ReminderType,
    Guid CategoryId, Guid? SubcategoryId, string? Provider, decimal? Cost, string? Currency, string BillingCycle, bool AutoRenew,
    string? LoginUrl);
public sealed record SubcategoryItem(Guid Id, string Code, string NameAr, string NameEn);
public sealed record CategoryItem(Guid Id, string Code, string NameAr, string NameEn, IReadOnlyList<SubcategoryItem> Subcategories);
public sealed record CategoriesResponse(IReadOnlyList<CategoryItem> Categories);

public sealed record CreateSubscriptionRequest(Guid? ClientId, string? ServiceName, DateOnly? EndsOn, Guid? CategoryId,
    Guid? SubcategoryId, string? Provider, decimal? Cost, string? Currency, string? BillingCycle, bool? AutoRenew, string? LoginUrl);

/// <summary>Editing a subscription: every field but its client, which a subscription does not change.</summary>
public sealed record UpdateSubscriptionRequest(string? ServiceName, DateOnly? EndsOn, Guid? CategoryId, Guid? SubcategoryId,
    string? Provider, decimal? Cost, string? Currency, string? BillingCycle, bool? AutoRenew, string? LoginUrl);

/// <summary>
/// The module's API (PROOF_SPEC T5; Amendment 02 §3): the client list, the subscription list, one subscription,
/// creating and editing one, and the classification catalog. Each runs its own unit of work on the module's context —
/// resolution first (3.5/6) — and requires its permission (5) before anything else. Both tenant lists carry the scope
/// declaration (3.5). The second axis itself is RLS beneath: no query here filters by scope.
/// </summary>
public static partial class SubscriptionEndpoints
{
    public const string Read = "subscriptions.read";
    public const string Write = "subscriptions.write";
    private const int MaxServiceName = 256;
    private const int MaxProvider = 256;
    private const int MaxLoginUrl = 2048;
    /// <summary>numeric(14,3): eleven digits before the point, three after (0012_classification.sql).</summary>
    private const decimal MaxCost = 99_999_999_999.999m;
    private static readonly string[] BillingCycles = ["monthly", "yearly"];

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

        // The classification (Amendment 02 §3.1): the platform's global catalog, the same for every tenant and every
        // member, so it carries no scope declaration — there is no scope it could be narrowed by. Read under
        // subscriptions.read (§5.2), through categories_read and subcategories_read. Active entries only, in the
        // order they were seeded.
        module.MapGet("/categories", (SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Read, t);
                var categories = await c.Categories.Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(t);
                var subcategories = await c.Subcategories.Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(t);
                return Results.Ok(new CategoriesResponse(categories.Select(x => new CategoryItem(x.Id, x.Code, x.NameAr, x.NameEn,
                    subcategories.Where(s => s.CategoryId == x.Id).Select(s => new SubcategoryItem(s.Id, s.Code, s.NameAr, s.NameEn)).ToList()))
                    .ToList()));
            }, ct));

        module.MapGet("", (int? offset, int? limit, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                var scope = await Permissions.RequireAsync(c, Read, t);
                return Results.Ok(await ScopedList.PageAsync(scope,
                    c.Subscriptions.OrderBy(x => x.Id).Select(x => new SubscriptionItem(x.Id, x.ScopeRefId, x.ServiceName, x.EndsOn, x.CategoryId)),
                    offset, limit, t));
            }, ct));

        // One subscription, every field. One the caller cannot see — another tenant's, or an unassigned client's — is
        // refused exactly as a write to it is (403 not_permitted): from outside, "not visible" and "not allowed" look
        // the same.
        module.MapGet("/{id:guid}", (Guid id, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Read, t);
                var s = await c.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, t);
                return s is null
                    ? Results.Json(new { error = ApiErrorCodes.NotPermitted }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.Ok(new SubscriptionDetail(s.Id, s.ScopeRefId, s.ServiceName, s.EndsOn, s.ReminderType, s.CategoryId,
                        s.SubcategoryId, s.Provider, s.Cost, s.Currency, s.BillingCycle, s.AutoRenew, s.LoginUrl));
            }, ct));

        // Creating a subscription: its client must be one the caller sees — client_scope's WITH CHECK refuses any
        // other (42501), and the composite FK any client of another tenant (23503).
        module.MapPost("", (CreateSubscriptionRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Write, t);
                if (body.ClientId is not { } client || Validate(body.ServiceName, body.EndsOn, body.CategoryId, body.Provider, body.Cost,
                        body.Currency, body.BillingCycle, body.AutoRenew, body.LoginUrl) is not { } fields)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                if (!await SubcategoryFitsAsync(c, fields.CategoryId, body.SubcategoryId, t))
                    return Results.Json(new { error = ApiErrorCodes.InvalidReference }, statusCode: StatusCodes.Status400BadRequest);
                var subscription = new Subscription
                {
                    Id = Guid.CreateVersion7(), TenantId = session.Current.TenantId!.Value, ScopeRefId = client, ReminderType = Subscription.Kind,
                    CreatedAt = DateTime.UtcNow,
                };
                fields.ApplyTo(subscription, body.SubcategoryId);
                c.Subscriptions.Add(subscription);
                await CriticalWrite.SaveAsync(c, "subscriptions insert", t);
                return Results.Json(new { id = subscription.Id }, statusCode: StatusCodes.Status201Created);
            }, ct));

        // Editing a subscription. One the caller cannot see is not found → CriticalWriteException → 403 not_permitted,
        // and nothing is written; the update itself passes the same two templates (and is audited automatically).
        module.MapPut("/{id:guid}", (Guid id, UpdateSubscriptionRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Write, t);
                if (Validate(body.ServiceName, body.EndsOn, body.CategoryId, body.Provider, body.Cost, body.Currency, body.BillingCycle,
                        body.AutoRenew, body.LoginUrl) is not { } fields)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var subscription = CriticalWrite.Require(await c.Subscriptions.SingleOrDefaultAsync(x => x.Id == id, t), "subscriptions update");
                if (!await SubcategoryFitsAsync(c, fields.CategoryId, body.SubcategoryId, t))
                    return Results.Json(new { error = ApiErrorCodes.InvalidReference }, statusCode: StatusCodes.Status400BadRequest);
                fields.ApplyTo(subscription, body.SubcategoryId);
                await CriticalWrite.SaveAsync(c, "subscriptions update", t);
                return Results.NoContent();
            }, ct));
    }

    /// <summary>The fields a create and an edit share, once each has passed its rule.</summary>
    private sealed record Fields(string ServiceName, DateOnly EndsOn, Guid CategoryId, string? Provider, decimal? Cost, string? Currency,
        string BillingCycle, bool AutoRenew, string? LoginUrl)
    {
        public void ApplyTo(Subscription s, Guid? subcategoryId)
        {
            s.ServiceName = ServiceName; s.EndsOn = EndsOn; s.CategoryId = CategoryId; s.SubcategoryId = subcategoryId;
            s.Provider = Provider; s.Cost = Cost; s.Currency = Currency; s.BillingCycle = BillingCycle; s.AutoRenew = AutoRenew;
            s.LoginUrl = LoginUrl;
        }
    }

    /// <summary>
    /// The request's own rules (§14: 400 invalid_value). The database holds the same closed catalogs and ranges in its
    /// CHECKs; these turn them into a refusal before any write. A cost needs its currency: the reminder states the
    /// amount with its currency or not at all (Amendment 02 E5, §2).
    /// </summary>
    private static Fields? Validate(string? serviceName, DateOnly? endsOn, Guid? categoryId, string? provider, decimal? cost,
        string? currency, string? billingCycle, bool? autoRenew, string? loginUrl)
    {
        if (serviceName is not { Length: > 0 and <= MaxServiceName } || endsOn is not { } ends || categoryId is not { } category
            || billingCycle is null || !BillingCycles.Contains(billingCycle) || autoRenew is not { } renew)
            return null;
        if (provider is { Length: 0 or > MaxProvider })
            return null;
        if (cost is { } amount && (amount < 0 || amount > MaxCost || decimal.Round(amount, 3) != amount))
            return null;
        if ((currency is not null && !CurrencyCode().IsMatch(currency)) || (cost is not null && currency is null))
            return null;
        if (loginUrl is not null && (loginUrl.Length > MaxLoginUrl || !Uri.TryCreate(loginUrl, UriKind.Absolute, out var url)
                || url.Scheme is not ("https" or "http")))
            return null;
        return new Fields(serviceName, ends, category, provider, cost, currency, billingCycle, renew, loginUrl);
    }

    /// <summary>
    /// A subcategory, when there is one, belongs to the subscription's category and is active. The two FKs hold each
    /// column alone (§3.2); this holds them together. An unknown category is the FK's (23503 → invalid_reference).
    /// </summary>
    private static async Task<bool> SubcategoryFitsAsync(SubscriptionsDbContext c, Guid categoryId, Guid? subcategoryId, CancellationToken ct) =>
        subcategoryId is not { } sub
        || await c.Subcategories.AnyAsync(x => x.Id == sub && x.CategoryId == categoryId && x.IsActive, ct);

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    private static Task<IResult> InTenantAsync(HttpContext http, SubscriptionsDbContext db, ISessionContextAccessor session,
        Func<SubscriptionsDbContext, CancellationToken, Task<IResult>> work, CancellationToken ct)
    {
        db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();   // recorded on the audit entries (7)
        if (session.Current.TenantId is null)
            return Task.FromResult(Results.Json(new { error = ApiErrorCodes.NoActiveTenant }, statusCode: StatusCodes.Status409Conflict));
        return UnitOfWork.RunAsync(db, session.Current, work, ct);
    }
}

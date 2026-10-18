using System.Globalization;
using System.Text.RegularExpressions;
using Core;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

public sealed record ClientItem(Guid Id, string Name, bool IsActive);
/// <summary>One client, every field (Amendment 01 §2.8) — what the client form reads before it writes.</summary>
public sealed record ClientDetail(Guid Id, string Name, string? Email, string? PhoneE164, string? TelegramChatId, bool IsActive);
public sealed record SubscriptionItem(Guid Id, Guid ClientId, string ServiceName, DateOnly EndsOn, Guid CategoryId, string Status);
/// <summary>
/// One subscription. <c>cost</c> is a decimal string ("12.500"), never a JSON number: a number is read by a browser
/// as binary floating point, and money must not pass through one (numeric(14,3) end to end).
/// </summary>
public sealed record SubscriptionDetail(Guid Id, Guid ClientId, string ServiceName, DateOnly EndsOn, string ReminderType,
    Guid CategoryId, Guid? SubcategoryId, string? Provider, string? Cost, string? Currency, string BillingCycle, bool AutoRenew,
    string? LoginUrl, string Status, Guid? RenewedFromId);
public sealed record SubcategoryItem(Guid Id, string Code, string NameAr, string NameEn);
public sealed record CategoryItem(Guid Id, string Code, string NameAr, string NameEn, IReadOnlyList<SubcategoryItem> Subcategories);
public sealed record CategoriesResponse(IReadOnlyList<CategoryItem> Categories);

public sealed record CreateSubscriptionRequest(Guid? ClientId, string? ServiceName, DateOnly? EndsOn, Guid? CategoryId,
    Guid? SubcategoryId, string? Provider, string? Cost, string? Currency, string? BillingCycle, bool? AutoRenew, string? LoginUrl);

/// <summary>
/// Creating or editing a client (Amendment 01 §2.8). <c>is_active</c> is not here: disabling is a different
/// permission on a different endpoint (§4.2, §5.1), so an operator who may edit a client cannot disable one by
/// sending a field.
/// </summary>
public sealed record ClientWriteRequest(string? Name, string? Email, string? PhoneE164, string? TelegramChatId);

/// <summary>Disabling a client, and enabling it again — <c>subscriptions.clients.manage</c> (§4.2).</summary>
public sealed record ClientStatusRequest(bool? IsActive);

/// <summary>
/// Renewing a subscription: the new end date, and nothing else. Every other field is inherited from the row being
/// renewed — above all its client. <see cref="ClientId"/> is read only to refuse it, exactly as
/// <see cref="UpdateSubscriptionRequest"/> does: a renewal that named a client would move the subscription between
/// clients through a second door, and it is answered 400 invalid_value rather than silently ignored.
/// </summary>
public sealed record RenewSubscriptionRequest(DateOnly? EndsOn, Guid? ClientId = null);

/// <summary>
/// Editing a subscription: every field but its client, which a subscription does not change. <see cref="ClientId"/>
/// is read only to refuse it: an edit that names a client is a move between clients — a scope crossing inside the
/// tenant — and is answered 400 invalid_value, loudly, rather than silently ignored. Never write it to the row.
/// </summary>
public sealed record UpdateSubscriptionRequest(string? ServiceName, DateOnly? EndsOn, Guid? CategoryId, Guid? SubcategoryId,
    string? Provider, string? Cost, string? Currency, string? BillingCycle, bool? AutoRenew, string? LoginUrl, Guid? ClientId = null);

/// <summary>
/// The module's API (PROOF_SPEC T5; Amendment 01 §5.1; Amendment 02 §3): the clients — listing, reading, creating,
/// editing, disabling and enabling one — the subscriptions — listing, reading, creating, editing, renewing and
/// cancelling one — and the classification catalog. Each runs its own unit of work on the module's context —
/// resolution first (3.5/6) — and requires its permission (5) before anything else. Both tenant lists carry the scope
/// declaration (3.5). The second axis itself is RLS beneath: no query here filters by scope. Nothing here deletes:
/// a client is disabled and a subscription is cancelled (Amendment 01 §2.8, §2.9).
/// </summary>
public static partial class SubscriptionEndpoints
{
    public const string Read = "subscriptions.read";
    public const string Write = "subscriptions.write";
    /// <summary>Creating and editing a client (Amendment 01 §4.2): owner, admin, operator.</summary>
    public const string ClientsWrite = "subscriptions.clients.write";
    /// <summary>Disabling a client and enabling it again (§4.2): owner and admin alone — not operator.</summary>
    public const string ClientsManage = "subscriptions.clients.manage";
    private const int MaxServiceName = 256;
    private const int MaxClientName = 256;
    private const int MaxEmail = 320;
    private const int MaxProvider = 256;
    private const int MaxLoginUrl = 2048;
    private static readonly string[] BillingCycles = ["monthly", "yearly"];

    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var module = app.MapGroup("/subscriptions").RequireAuthorization().WithMetadata(new OwnUnitsOfWorkAttribute());

        module.MapGet("/clients", (int? offset, int? limit, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                var scope = await Permissions.RequireAsync(c, Read, t);
                return Results.Ok(await ScopedList.PageAsync(scope,
                    c.Clients.OrderBy(x => x.Id).Select(x => new ClientItem(x.Id, x.Name, x.IsActive)), offset, limit, t));
            }, ct));

        // One client, every field — what the client form reads before it writes. A client the caller cannot see is
        // refused exactly as a write to it is (403 not_permitted), as the subscription's own detail is.
        module.MapGet("/clients/{id:guid}", (Guid id, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Read, t);
                var client = await c.Clients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, t);
                return client is null
                    ? Results.Json(new { error = ApiErrorCodes.NotPermitted }, statusCode: StatusCodes.Status403Forbidden)
                    : Results.Ok(new ClientDetail(client.Id, client.Name, client.Email, client.PhoneE164, client.TelegramChatId, client.IsActive));
            }, ct));

        // Creating a client. A client is its own scoped entity, and a new row has no assignment by virtue of being
        // new, so client_scope's WITH CHECK refuses the insert from a member in mode 'assigned' (42501 → 403). An
        // operator in mode 'all' creates; an assigned one edits the clients assigned to them and creates none. That
        // is the second axis narrowing the permission, proven on PostgreSQL 18.6 and written into Amendment 01 §4.2.
        module.MapPost("/clients", (ClientWriteRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, ClientsWrite, t);
                if (ValidateClient(body) is not { } fields)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var id = Guid.CreateVersion7();
                var client = new Client
                {
                    Id = id, TenantId = session.Current.TenantId!.Value, ScopeRefId = id,   // the synonym (0008)
                    CreatedAt = DateTime.UtcNow, IsActive = true,
                };
                fields.ApplyTo(client);
                c.Clients.Add(client);
                await CriticalWrite.SaveAsync(c, "clients insert", t);
                return Results.Json(new { id = client.Id }, statusCode: StatusCodes.Status201Created);
            }, ct));

        // Editing a client: its name and the three channels, never its is_active — disabling is clients.manage on
        // the endpoint below. One the caller cannot see is not found → CriticalWriteException → 403 not_permitted.
        module.MapPut("/clients/{id:guid}", (Guid id, ClientWriteRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, ClientsWrite, t);
                if (ValidateClient(body) is not { } fields)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var client = CriticalWrite.Require(await c.Clients.SingleOrDefaultAsync(x => x.Id == id, t), "clients update");
                fields.ApplyTo(client);
                await CriticalWrite.SaveAsync(c, "clients update", t);
                return Results.NoContent();
            }, ct));

        // Disabling a client and enabling it again — subscriptions.clients.manage (owner, admin). The endpoint is
        // what refuses an operator, not the screen. Disabling changes nothing about the client's subscriptions:
        // their status, dates and history stay as they are, and stage (d) reads is_active when it sends.
        // Setting the value a client already has writes nothing and answers 204: EF issues no UPDATE for an
        // unchanged row, so the request is idempotent and earns no audit entry for a change that did not happen.
        module.MapPut("/clients/{id:guid}/status", (Guid id, ClientStatusRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, ClientsManage, t);
                if (body.IsActive is not { } isActive)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var client = CriticalWrite.Require(await c.Clients.SingleOrDefaultAsync(x => x.Id == id, t), "clients status");
                client.IsActive = isActive;
                await CriticalWrite.SaveAsync(c, "clients status", t);
                return Results.NoContent();
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
                    c.Subscriptions.OrderBy(x => x.Id).Select(x => new SubscriptionItem(x.Id, x.ScopeRefId, x.ServiceName, x.EndsOn, x.CategoryId, x.Status)),
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
                        s.SubcategoryId, s.Provider, s.Cost?.ToString("0.000", CultureInfo.InvariantCulture), s.Currency, s.BillingCycle, s.AutoRenew, s.LoginUrl,
                        s.Status, s.RenewedFromId));
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
                    CreatedAt = DateTime.UtcNow, Status = Subscription.Active, RenewedFromId = null,
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
                if (body.ClientId is not null || Validate(body.ServiceName, body.EndsOn, body.CategoryId, body.Provider, body.Cost,
                        body.Currency, body.BillingCycle, body.AutoRenew, body.LoginUrl) is not { } fields)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var subscription = CriticalWrite.Require(await c.Subscriptions.SingleOrDefaultAsync(x => x.Id == id, t), "subscriptions update");
                // A cancelled or renewed subscription is not edited: subscriptions_status_gate lets no UPDATE reach
                // it, and without this the write would surface as 403 not_permitted on a row the caller can plainly
                // read. The state is the reason, so the state is the code.
                if (subscription.Status != Subscription.Active)
                    return Results.Json(new { error = ApiErrorCodes.SubscriptionNotActive }, statusCode: StatusCodes.Status409Conflict);
                if (!await SubcategoryFitsAsync(c, fields.CategoryId, body.SubcategoryId, t))
                    return Results.Json(new { error = ApiErrorCodes.InvalidReference }, statusCode: StatusCodes.Status400BadRequest);
                fields.ApplyTo(subscription, body.SubcategoryId);
                await CriticalWrite.SaveAsync(c, "subscriptions update", t);
                return Results.NoContent();
            }, ct));

        // Renewing a subscription (Amendment 01 §2.9): a NEW row pointing at the old one through renewed_from_id,
        // never an extension of it. Everything but the end date is inherited from the row being renewed — above all
        // scope_ref_id, which is never read from the request: stage (a) refused an edit that names a client, and a
        // renewal that accepted one would be the same crossing by another door. The old row becomes 'renewed' in
        // the same transaction; one subscription is renewed once, and the UNIQUE on (tenant_id, renewed_from_id)
        // is what holds that when this code does not.
        module.MapPost("/{id:guid}/renew", (Guid id, RenewSubscriptionRequest body, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Write, t);
                if (body.ClientId is not null || body.EndsOn is not { } endsOn)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var previous = CriticalWrite.Require(await c.Subscriptions.SingleOrDefaultAsync(x => x.Id == id, t), "subscriptions renew");
                if (previous.Status != Subscription.Active)
                    return Results.Json(new { error = ApiErrorCodes.SubscriptionNotActive }, statusCode: StatusCodes.Status409Conflict);
                // A renewal runs past the term it renews; the same date or an earlier one is a typo, not a renewal.
                if (endsOn <= previous.EndsOn)
                    return Results.Json(new { error = ApiErrorCodes.InvalidValue }, statusCode: StatusCodes.Status400BadRequest);
                var renewal = new Subscription
                {
                    Id = Guid.CreateVersion7(), TenantId = previous.TenantId, ScopeRefId = previous.ScopeRefId,
                    ServiceName = previous.ServiceName, EndsOn = endsOn, CreatedAt = DateTime.UtcNow,
                    ReminderType = previous.ReminderType, CategoryId = previous.CategoryId, SubcategoryId = previous.SubcategoryId,
                    Provider = previous.Provider, Cost = previous.Cost, Currency = previous.Currency,
                    BillingCycle = previous.BillingCycle, AutoRenew = previous.AutoRenew, LoginUrl = previous.LoginUrl,
                    Status = Subscription.Active, RenewedFromId = previous.Id,
                };
                previous.Status = Subscription.Renewed;
                c.Subscriptions.Add(renewal);
                await CriticalWrite.SaveAsync(c, "subscriptions renew", t);
                return Results.Json(new { id = renewal.Id }, statusCode: StatusCodes.Status201Created);
            }, ct));

        // Cancelling a subscription: a status change, never a delete (§2.9). The row, its dates and its history
        // stay; nothing about it is removed and nothing about its client changes.
        module.MapPost("/{id:guid}/cancel", (Guid id, SubscriptionsDbContext db, ISessionContextAccessor session, HttpContext http, CancellationToken ct) =>
            InTenantAsync(http, db, session, async (c, t) =>
            {
                await Permissions.RequireAsync(c, Write, t);
                var subscription = CriticalWrite.Require(await c.Subscriptions.SingleOrDefaultAsync(x => x.Id == id, t), "subscriptions cancel");
                if (subscription.Status != Subscription.Active)
                    return Results.Json(new { error = ApiErrorCodes.SubscriptionNotActive }, statusCode: StatusCodes.Status409Conflict);
                subscription.Status = Subscription.Cancelled;
                await CriticalWrite.SaveAsync(c, "subscriptions cancel", t);
                return Results.NoContent();
            }, ct));
    }

    /// <summary>A client's fields, once they have passed their rule.</summary>
    private sealed record ClientFields(string Name, string? Email, string? PhoneE164, string? TelegramChatId)
    {
        public void ApplyTo(Client client)
        {
            client.Name = Name; client.Email = Email; client.PhoneE164 = PhoneE164; client.TelegramChatId = TelegramChatId;
        }
    }

    /// <summary>
    /// A client's own rules (§14: 400 invalid_value), the same closed shapes the database holds as CHECKs, refused
    /// here before any write. All three channels are optional — a client reachable by one is not reachable by three
    /// — but an empty string is not "no channel": it is a value that matches nothing, so it is refused rather than
    /// stored. A phone is E.164 and nothing else, so one line has one spelling across the tenant; a Telegram chat
    /// id is a signed integer in text, because a group's id is negative.
    /// </summary>
    private static ClientFields? ValidateClient(ClientWriteRequest body)
    {
        if (body.Name is not { Length: > 0 and <= MaxClientName } name)
            return null;
        if (body.Email is not null && (body.Email.Length > MaxEmail || !EmailShape().IsMatch(body.Email)))
            return null;
        if (body.PhoneE164 is not null && !PhoneE164().IsMatch(body.PhoneE164))
            return null;
        if (body.TelegramChatId is not null && !TelegramChatId().IsMatch(body.TelegramChatId))
            return null;
        return new ClientFields(name, body.Email, body.PhoneE164, body.TelegramChatId);
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
    /// amount with its currency or not at all (Amendment 02 E5, §2). The cost arrives as a decimal string, parsed straight
    /// to decimal: numeric(14,3), up to eleven digits before the point and three after, no sign, no exponent.
    /// </summary>
    private static Fields? Validate(string? serviceName, DateOnly? endsOn, Guid? categoryId, string? provider, string? costText,
        string? currency, string? billingCycle, bool? autoRenew, string? loginUrl)
    {
        if (serviceName is not { Length: > 0 and <= MaxServiceName } || endsOn is not { } ends || categoryId is not { } category
            || billingCycle is null || !BillingCycles.Contains(billingCycle) || autoRenew is not { } renew)
            return null;
        if (provider is { Length: 0 or > MaxProvider })
            return null;
        decimal? cost = null;
        if (costText is not null)
        {
            if (!CostText().IsMatch(costText))
                return null;
            cost = decimal.Parse(costText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
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

    [GeneratedRegex("^[0-9]{1,11}([.][0-9]{1,3})?$")]   // ASCII digits only: \d would match ٣ too
    private static partial Regex CostText();

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();

    // The three client shapes, each the same text as its CHECK in 0013_clients_and_lifecycle.sql. ASCII digits
    // only, for the same reason the cost's pattern gives: \d would match ٣ too.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+[.][^@\s]+$")]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^\+[1-9][0-9]{1,14}$")]
    private static partial Regex PhoneE164();

    [GeneratedRegex("^-?[0-9]{1,20}$")]
    private static partial Regex TelegramChatId();

    private static Task<IResult> InTenantAsync(HttpContext http, SubscriptionsDbContext db, ISessionContextAccessor session,
        Func<SubscriptionsDbContext, CancellationToken, Task<IResult>> work, CancellationToken ct)
    {
        db.ClientAddress = http.Connection.RemoteIpAddress?.ToString();   // recorded on the audit entries (7)
        if (session.Current.TenantId is null)
            return Task.FromResult(Results.Json(new { error = ApiErrorCodes.NoActiveTenant }, statusCode: StatusCodes.Status409Conflict));
        return UnitOfWork.RunAsync(db, session.Current, work, ct);
    }
}

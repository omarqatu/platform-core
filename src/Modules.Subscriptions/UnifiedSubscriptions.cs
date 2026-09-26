using System.Text;
using System.Text.Json;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Modules.Subscriptions;

public sealed record UnifiedItem(Guid TenantId, Guid Id, Guid ClientId, string ServiceName, DateOnly EndsOn);

/// <summary>A tenant's declaration in the merged result (6.4): its status, and its own scope declaration (3.5).</summary>
public sealed record UnifiedTenant(Guid TenantId, string Name, string Status, string? ScopeMode, long? VisibleCount, long? TotalCount);

public sealed record UnifiedPage(IReadOnlyList<UnifiedTenant> Tenants, IReadOnlyList<UnifiedItem> Items, bool HasMore, string? NextCursor);

/// <summary>
/// The cross-tenant unified view of subscriptions (PLATFORM_CORE 6; PROOF_SPEC T6): a fan-out over the user's
/// tenants (TenantFanOut — a read-only transaction per tenant, its own resolution, batches of at most the cap), a
/// keyset page per tenant on (ends_on, id) after that tenant's cursor, and a k-way merge. "The next page" advances the
/// cursors of the tenants whose items were consumed — never a global offset (6.3). Every tenant runs on every page,
/// batch after batch (the project owner's reading of 6.3 in T6): the merged order is global, and memory is bounded by
/// tenants × (limit + 1) rows. The result declares each tenant's scope: total_count only under scope_all (6.4).
/// Read-only: GET only, and each tenant's transaction is READ ONLY in the database.
/// </summary>
public static class UnifiedSubscriptions
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;

    public static void MapUnifiedSubscriptions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/unified/subscriptions", async (int? limit, string? cursor, SubscriptionsDbContext db,
            Microsoft.EntityFrameworkCore.DbContextOptions<SubscriptionsDbContext> options, ISessionContextAccessor session,
            IConfiguration configuration, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (!Cursor.TryParse(cursor, out var cursors))
                return Results.Json(new { error = "invalid_cursor" }, statusCode: StatusCodes.Status400BadRequest);
            var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
            var cap = configuration.GetValue("UnifiedView:TenantBatchCap", TenantFanOut.DefaultBatchCap);
            var user = session.Current.UserId!.Value;

            var tenants = await TenantFanOut.MemberTenantsAsync(db, user, ct);
            var pending = tenants.Where(t => !(cursors.TryGetValue(t.TenantId, out var c) && c.Exhausted)).ToList();
            var results = await TenantFanOut.RunAsync(pending, cap, () => new SubscriptionsDbContext(options), user,
                (c, tenant, t) => TenantPageAsync(c, cursors.GetValueOrDefault(tenant.TenantId), take, t),
                loggers.CreateLogger("UnifiedView"), ct);
            return Results.Ok(Merge(tenants, results, cursors, take));
        }).RequireAuthorization().WithMetadata(new OwnUnitsOfWorkAttribute());
    }

    private sealed record TenantRows(ResolvedScope Scope, long Visible, List<UnifiedItem> Rows);

    // One tenant, in its own transaction: the permission (5), the declaration's count, and up to take + 1 rows after
    // the tenant's cursor — so the merge knows whether the tenant has more.
    private static async Task<TenantRows> TenantPageAsync(SubscriptionsDbContext db, TenantCursor? after, int take, CancellationToken ct)
    {
        var scope = await Permissions.RequireAsync(db, SubscriptionEndpoints.Read, ct);
        var visible = await db.Subscriptions.LongCountAsync(ct);
        var query = db.Subscriptions.AsQueryable();
        if (after is { EndsOn: { } endsOn, Id: { } id })
            query = query.Where(s => s.EndsOn > endsOn || (s.EndsOn == endsOn && s.Id.CompareTo(id) > 0));
        var rows = await query.OrderBy(s => s.EndsOn).ThenBy(s => s.Id).Take(take + 1)
            .Select(s => new UnifiedItem(s.TenantId, s.Id, s.ScopeRefId, s.ServiceName, s.EndsOn)).ToListAsync(ct);
        return new TenantRows(scope, visible, rows);
    }

    private static UnifiedPage Merge(List<MemberTenant> tenants, List<TenantResult<TenantRows>> results,
        Dictionary<Guid, TenantCursor> cursors, int take)
    {
        var ok = results.Where(r => r.Status == "ok").ToDictionary(r => r.Tenant.TenantId, r => r.Value!);
        var page = ok.Values.SelectMany(v => v.Rows).OrderBy(r => r, ItemOrder.Instance).Take(take).ToList();

        var next = new Dictionary<Guid, TenantCursor>(cursors);
        foreach (var (tenant, rows) in ok)
        {
            var consumed = page.Where(i => i.TenantId == tenant).ToList();
            var exhausted = rows.Rows.Count <= take && consumed.Count == rows.Rows.Count;
            next[tenant] = consumed.Count > 0
                ? new TenantCursor(consumed[^1].EndsOn, consumed[^1].Id, exhausted)
                : (cursors.GetValueOrDefault(tenant) ?? new TenantCursor(null, null, false)) with { Exhausted = exhausted };
        }
        var hasMore = ok.Keys.Any(t => !next[t].Exhausted);

        var declared = tenants.Select(t =>
        {
            var result = results.FirstOrDefault(r => r.Tenant.TenantId == t.TenantId);
            if (result is null)   // exhausted on an earlier page: not queried again
                return new UnifiedTenant(t.TenantId, t.Name, "exhausted", null, null, null);
            if (result.Status != "ok")
                return new UnifiedTenant(t.TenantId, t.Name, result.Status, null, null, null);
            var v = result.Value!;
            return new UnifiedTenant(t.TenantId, t.Name, "ok", v.Scope.ScopeAll ? "all" : "assigned", v.Visible,
                v.Scope.ScopeAll ? v.Visible : null);
        }).ToList();
        return new UnifiedPage(declared, page, hasMore, hasMore ? Cursor.Format(next) : null);
    }

    /// <summary>(ends_on, id), with ids compared as PostgreSQL compares uuid — byte by byte, big-endian.</summary>
    private sealed class ItemOrder : IComparer<UnifiedItem>
    {
        public static readonly ItemOrder Instance = new();

        public int Compare(UnifiedItem? x, UnifiedItem? y)
        {
            var byDate = x!.EndsOn.CompareTo(y!.EndsOn);
            return byDate != 0 ? byDate : Cursor.CompareIds(x.Id, y.Id);
        }
    }
}

/// <summary>A tenant's keyset position: the last consumed (ends_on, id), and whether it has nothing more.</summary>
public sealed record TenantCursor(DateOnly? EndsOn, Guid? Id, bool Exhausted);

/// <summary>
/// The opaque page cursor: one keyset position per tenant, base64url-encoded JSON. It names tenants only as positions;
/// the tenants queried are always the caller's own memberships, read on the request — an entry for any other tenant
/// is ignored, and no scope or permission ever comes from it (3.5/6).
/// </summary>
public static class Cursor
{
    public static bool TryParse(string? text, out Dictionary<Guid, TenantCursor> cursors)
    {
        cursors = [];
        if (string.IsNullOrEmpty(text))
            return true;
        try
        {
            var padded = text.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            cursors = JsonSerializer.Deserialize<Dictionary<Guid, TenantCursor>>(Encoding.UTF8.GetString(Convert.FromBase64String(padded))) ?? [];
            return true;
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    public static string Format(Dictionary<Guid, TenantCursor> cursors) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursors)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static int CompareIds(Guid x, Guid y) => x.ToByteArray(bigEndian: true).AsSpan().SequenceCompareTo(y.ToByteArray(bigEndian: true));
}

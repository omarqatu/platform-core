using Microsoft.EntityFrameworkCore;

namespace Core.Data;

/// <summary>
/// The scope-declaration contract (PLATFORM_CORE 3.5, 1.8; PROOF_SPEC T5, spec item m) — every aggregate response
/// carries its effective scope:
/// <list type="bullet">
/// <item>scope_mode — always, 'all' or 'assigned', from the scope resolved on this transaction (3.5/6).</item>
/// <item>visible_count — always: the items after every constraint together (scope, permission, status, search),
/// across all pages.</item>
/// <item>total_count — the tenant's total under the same constraints except scope, only under scope_all; an
/// explicit null otherwise, never an absent field (Test 19). Under scope_all the scope removes nothing, so it is
/// the same count; no read ever runs outside the caller's scope to produce it.</item>
/// <item>has_more_in_scope — more items within the scope after this page.</item>
/// </list>
/// </summary>
public sealed record ScopedList<T>(string ScopeMode, long VisibleCount, long? TotalCount, bool HasMoreInScope, IReadOnlyList<T> Items);

public static class ScopedList
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    /// <summary>
    /// One page of <paramref name="ordered"/> — already carrying every constraint but scope, which RLS adds beneath,
    /// and a stable order (ordered before any projection) — with its declaration.
    /// </summary>
    public static async Task<ScopedList<T>> PageAsync<T>(ResolvedScope scope, IQueryable<T> ordered, int? offset, int? limit,
        CancellationToken ct)
    {
        var skip = Math.Max(offset ?? 0, 0);
        var take = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);
        var visible = await ordered.LongCountAsync(ct);
        var rows = await ordered.Skip(skip).Take(take + 1).ToListAsync(ct);
        return new ScopedList<T>(
            scope.ScopeAll ? "all" : "assigned",
            visible,
            scope.ScopeAll ? visible : null,
            rows.Count > take,
            rows.Take(take).ToList());
    }
}

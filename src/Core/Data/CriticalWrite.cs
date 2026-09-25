using Microsoft.EntityFrameworkCore;

namespace Core.Data;

/// <summary>
/// The rows-affected guard (3.5/5): a critical write that affects a different
/// number of rows than expected — typically zero, because the row is invisible
/// under RLS — throws instead of succeeding silently (Test 14).
/// Tracked-entity updates through SaveChanges already get this from EF, which
/// throws DbUpdateConcurrencyException when a modified row affects zero rows;
/// <see cref="SaveAsync"/> reports that as this guard's error, and
/// <see cref="Require{T}"/> does the same for a row the read before it did not see.
/// Tracked writes are what the automatic audit sees (7) — bulk commands are not.
/// </summary>
public static class CriticalWrite
{
    /// <summary>A tracked critical write: every modified row must be written, or this guard's error.</summary>
    public static async Task<int> SaveAsync(DbContext db, string description, CancellationToken cancellationToken)
    {
        try
        {
            return await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException error)
        {
            throw new CriticalWriteException(description, error.Entries.Count, 0, error);
        }
    }

    /// <summary>The row a critical write loads first: invisible under RLS → zero rows affected, loudly.</summary>
    public static T Require<T>(T? row, string description) where T : class =>
        row ?? throw new CriticalWriteException(description, 1, 0);

    public static async Task<int> ExpectRowsAsync(Task<int> write, int expected, string description)
    {
        var affected = await write;
        if (affected != expected)
            throw new CriticalWriteException(description, expected, affected);
        return affected;
    }
}

public sealed class CriticalWriteException(string description, int expected, int affected, Exception? inner = null)
    : InvalidOperationException(
        $"Critical write '{description}' affected {affected} row(s); expected {expected} (PLATFORM_CORE 3.5/5).", inner)
{
    public int Expected { get; } = expected;
    public int Affected { get; } = affected;
}

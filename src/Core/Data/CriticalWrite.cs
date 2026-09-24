namespace Core.Data;

/// <summary>
/// The rows-affected guard (3.5/5): a critical write that affects a different
/// number of rows than expected — typically zero, because the row is invisible
/// under RLS — throws instead of succeeding silently (Test 14).
/// Tracked-entity updates through SaveChanges already get this from EF, which
/// throws DbUpdateConcurrencyException when a modified row affects zero rows.
/// </summary>
public static class CriticalWrite
{
    public static async Task<int> ExpectRowsAsync(Task<int> write, int expected, string description)
    {
        var affected = await write;
        if (affected != expected)
            throw new CriticalWriteException(description, expected, affected);
        return affected;
    }
}

public sealed class CriticalWriteException(string description, int expected, int affected)
    : InvalidOperationException(
        $"Critical write '{description}' affected {affected} row(s); expected {expected} (PLATFORM_CORE 3.5/5).")
{
    public int Expected { get; } = expected;
    public int Affected { get; } = affected;
}

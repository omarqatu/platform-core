using System.Diagnostics;

namespace Core.Identity;

/// <summary>
/// Holding an answer until a fixed point, so its timing carries no information (OPEN_ITEMS 51).
/// <para>
/// A path that answers the same thing for two different states still tells them apart if it answers at two
/// different times. The fix is a floor: the work is done, and then the answer waits until the floor has passed
/// from the moment the request arrived. It is honest only while the work is shorter than the floor and bounded —
/// which is why the e-mail provider is not in the path that uses this (<c>INotificationDispatch</c> takes it) and
/// why overrunning the floor is logged by the caller rather than hidden.
/// </para>
/// </summary>
public static class UniformTiming
{
    /// <summary>
    /// Waits until <paramref name="floor"/> has elapsed since <paramref name="startedAt"/>, then for
    /// <paramref name="extra"/> on top — the limiter's escalating delay, which must itself be the same function
    /// of the same inputs in every case, or the hold is pointless.
    /// </summary>
    /// <returns>How long the work took, for a caller that wants to notice it overrunning the floor.</returns>
    public static async Task<TimeSpan> HoldAsync(
        long startedAt, TimeSpan floor, TimeSpan extra, CancellationToken cancellationToken)
    {
        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        var remaining = floor - elapsed + extra;
        if (remaining > TimeSpan.Zero)
            await Task.Delay(remaining, cancellationToken);
        return elapsed;
    }
}

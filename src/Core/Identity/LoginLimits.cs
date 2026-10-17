namespace Core.Identity;

/// <summary>
/// The attempt limiter's numbers (Amendment 01 §5.2, OPEN_ITEMS 16): an escalating delay per (username + ip),
/// <b>no account lockout</b> — a lockout lets anyone lock out a known user — and a ceiling per ip above which the
/// request is refused before the password is read. Every number here is configuration, and every one is a marked
/// assumption <c>[—]</c> until the project owner settles it at launch (§7/2 of the amendment, OPEN_ITEMS 16).
/// Bound from the configuration section <c>Identity:LoginLimits</c>.
/// </summary>
public sealed class LoginLimits
{
    /// <summary>The window the failed attempts are counted in.</summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Failures in the window that cost nothing: a mistyped password twice is not an attack.</summary>
    public int FreeFailures { get; set; } = 2;

    /// <summary>
    /// The escalation after the free ones, in seconds: the first value is the delay on the failure after the last
    /// free one, and the last value is the cap for everything beyond it. Empty means no delay at all.
    /// </summary>
    public double[] DelaySecondsByFailure { get; set; } = [1, 2, 4, 8, 15];

    /// <summary>
    /// Failures from one ip within the window at or above which the request is refused with 429, before the
    /// password is read. Zero or less: no ceiling.
    /// </summary>
    public int PerIpCeiling { get; set; } = 50;

    /// <summary>
    /// What this attempt waits for, given the failures already in the window for its (username + ip).
    /// </summary>
    public TimeSpan DelayFor(int failuresBefore)
    {
        var step = failuresBefore - Math.Max(FreeFailures, 0);
        if (step < 0 || DelaySecondsByFailure.Length == 0)
            return TimeSpan.Zero;
        return TimeSpan.FromSeconds(DelaySecondsByFailure[Math.Min(step, DelaySecondsByFailure.Length - 1)]);
    }
}

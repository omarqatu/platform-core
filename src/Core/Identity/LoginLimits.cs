using System.Globalization;

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

    /// <summary>The escalation when configuration gives none (the key absent, or empty).</summary>
    public static readonly IReadOnlyList<double> DefaultDelaySeconds = [1, 2, 4, 8, 15];

    /// <summary>
    /// The escalation after the free ones, in seconds, as one comma-separated value ("1,2,4,8,15"): the first is
    /// the delay on the failure after the last free one, and the last is the cap for everything beyond it.
    /// One value, never a list, because a list is read wrong twice over (OPEN_ITEMS 91): the binder appends to an
    /// array the property already holds, and configuration layers merge a list index by index, so a shorter
    /// sequence set over a longer one would keep the longer one's tail. One key replaces cleanly in every layer.
    /// Read it through <see cref="EffectiveDelaySeconds"/>.
    /// </summary>
    public string? DelaySecondsByFailure { get; set; }

    /// <summary>
    /// The escalation in force: the configured one, or <see cref="DefaultDelaySeconds"/> when there is none. An
    /// absent or empty value is "none", not "no delay": the limiter does not fail open on a configuration that says
    /// nothing. A malformed one throws — Api checks it at startup and refuses to start (fail-closed).
    /// </summary>
    public IReadOnlyList<double> EffectiveDelaySeconds() => ParseDelaySeconds(DelaySecondsByFailure);

    /// <summary>
    /// "1,2,4,8,15" → [1, 2, 4, 8, 15]; null, empty or blank → the default. Every element must be a plain decimal
    /// number of seconds, more than zero and at least the one before it — the delay escalates (Amendment 01 §5.2):
    /// an empty, negative, zero, non-numeric or falling element is an error, never skipped and never replaced by a
    /// default.
    /// </summary>
    public static IReadOnlyList<double> ParseDelaySeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultDelaySeconds;
        var elements = value.Split(',');
        var delays = new double[elements.Length];
        for (var i = 0; i < elements.Length; i++)
        {
            // No sign is allowed, so a negative delay does not parse; NaN and infinity are refused explicitly.
            if (!double.TryParse(elements[i].Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var delay)
                || !double.IsFinite(delay))
                throw new FormatException(
                    $"Identity:LoginLimits:DelaySecondsByFailure is '{value}': element {i + 1} ('{elements[i].Trim()}') " +
                    "is not a number of seconds. The value is comma-separated seconds, e.g. \"1,2,4,8,15\".");
            if (delay <= 0 || (i > 0 && delay < delays[i - 1]))
                throw new FormatException(
                    $"Identity:LoginLimits:DelaySecondsByFailure is '{value}': element {i + 1} ({delay}) is not more than " +
                    "zero and at least the one before it. The delay escalates (Amendment 01 §5.2), e.g. \"1,2,4,8,15\".");
            delays[i] = delay;
        }
        return delays;
    }

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
        if (step < 0)
            return TimeSpan.Zero;
        var delays = EffectiveDelaySeconds();
        return TimeSpan.FromSeconds(delays[Math.Min(step, delays.Count - 1)]);
    }
}

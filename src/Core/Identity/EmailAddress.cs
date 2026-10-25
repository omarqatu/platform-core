using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Core.Identity;

/// <summary>
/// The one rule two addresses are compared under (§4.5; OPEN_ITEMS 41): surrounding spaces removed, case unified —
/// the expression of persons_email_normalized_key, lower(btrim(email)) (migration 0009). btrim with no characters
/// removes spaces only, so <see cref="Normalize"/> trims spaces only, not every whitespace character.
/// <para>
/// And the one rule an address is <b>sendable</b> under (the project owner's item 35, group ج): the shape the database
/// already holds a client's address to, and exactly one recipient once parsed. Two invitations to <c>adnan</c> and
/// <c>hasan</c> reached production, and .NET's <c>MailMessage</c> reads <c>a@x.com, b@y.com</c> as two recipients and
/// sends to both — so the shape alone is not enough, and the parse must give back the input itself.
/// </para>
/// </summary>
public static class EmailAddress
{
    /// <summary>
    /// <c>clients_email_check</c>'s pattern, as the catalog prints it (EmailShapeTests compares the two character for
    /// character, so they cannot drift). PostgreSQL reads <c>[:space:]</c> inside a bracket as a class; .NET reads it
    /// as seven literal characters, so <see cref="Shape"/> substitutes <c>\s</c> for it — the one declared difference:
    /// .NET's <c>\s</c> also matches Unicode spaces, so the application is <b>stricter</b> than the constraint, never
    /// looser (DEVELOPER_GUIDE §15).
    /// </summary>
    public const string ShapePattern = "^[^@[:space:]]+@[^@[:space:]]+[.][^@[:space:]]+$";

    /// <summary><c>clients_email_check</c>'s length bound.</summary>
    public const int MaxLength = 320;

    public static string Normalize(string email) => email.Trim(' ').ToLowerInvariant();

    public static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="email"/>, its surrounding spaces aside, is one deliverable address: within
    /// <see cref="MaxLength"/>, of <see cref="ShapePattern"/>'s shape, and parsed by <see cref="MailAddress"/> into
    /// exactly that text — no display name, no list, nothing dropped before a space.
    /// </summary>
    public static bool IsSendable(string email)
    {
        var trimmed = email.Trim(' ');
        return trimmed.Length is > 0 and <= MaxLength
               && Shape().IsMatch(trimmed)
               && MailAddress.TryCreate(trimmed, out var parsed)
               && string.Equals(parsed.Address, trimmed, StringComparison.Ordinal)
               && string.IsNullOrEmpty(parsed.DisplayName);
    }

    /// <summary><see cref="ShapePattern"/> with its one substitution, derived and not retyped.</summary>
    private static readonly Regex ShapeRegex = new(ShapePattern.Replace("[:space:]", @"\s", StringComparison.Ordinal),
        RegexOptions.CultureInvariant);

    private static Regex Shape() => ShapeRegex;
}

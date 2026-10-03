namespace Core.Identity;

/// <summary>
/// The one rule two addresses are compared under (§4.5; OPEN_ITEMS 41): surrounding spaces removed, case unified —
/// the expression of persons_email_normalized_key, lower(btrim(email)) (migration 0009). btrim with no characters
/// removes spaces only, so <see cref="Normalize"/> trims spaces only, not every whitespace character.
/// </summary>
public static class EmailAddress
{
    public static string Normalize(string email) => email.Trim(' ').ToLowerInvariant();

    public static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
}

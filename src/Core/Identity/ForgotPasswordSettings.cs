namespace Core.Identity;

/// <summary>
/// <c>POST /api/auth/password/forgot</c> (Amendment 01 §5.1), bound from <c>Identity:ForgotPassword</c>.
/// Both numbers are marked assumptions <c>[—]</c> until the project owner settles them (§7/2, OPEN_ITEMS 54).
/// </summary>
public sealed class ForgotPasswordSettings
{
    /// <summary>How long a recovery token stays usable. The roadmap's figure (M2/c): 30 minutes.</summary>
    public TimeSpan TokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The floor every answer waits for, measured from the first line of the endpoint (OPEN_ITEMS 51). The
    /// response must not say, by its timing, whether the address has an account; the work that differs between
    /// the two cases is a token insert and the consumption of any outstanding token, and this floor covers it.
    /// The provider round trip is not covered and is not in the path at all — the dispatcher takes it
    /// (<c>Core.Notifications.INotificationDispatch</c>).
    /// <para>
    /// Zero disables the floor, which re-opens the leak; it exists so a test can run without waiting.
    /// </para>
    /// </summary>
    public TimeSpan MinimumResponseTime { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// The link the e-mail carries, with <c>{token}</c> where the token goes. The token stays in the fragment,
    /// so it never reaches a server log or a Referer header (the invitation link's rule, DEVELOPER_GUIDE §9).
    /// The screen it points at is <c>web/src/screens/password/ResetPasswordScreen.tsx</c>, which reads the token
    /// out of the fragment before anything renders (OPEN_ITEMS 59, closed).
    /// </summary>
    public string ResetLinkTemplate { get; set; } = "/app/password/reset#token={token}";

    public string ResetLinkFor(string token) => ResetLinkTemplate.Replace("{token}", Uri.EscapeDataString(token));
}

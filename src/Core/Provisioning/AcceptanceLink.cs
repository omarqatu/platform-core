namespace Core.Provisioning;

/// <summary>
/// The one definition of the invitation acceptance link (DEVELOPER_GUIDE §9), and of the origin it is built on.
/// <para>
/// Two writers need it and must not drift apart: <c>Ops create-tenant</c>, which prints the owner's link, and
/// <c>POST /api/members/invitations</c>, which puts a member's link in an e-mail. The shape matters —
/// <c>#tenant=…&amp;token=…</c> is a **fragment**, which a browser never sends to a server, so the token reaches no
/// access log and no <c>Referer</c> header. The screen behind it reads the fragment into memory before anything
/// renders and clears the address (<c>web/src/invitations/pendingInvitation.ts</c>).
/// </para>
/// <para>
/// The origin is configuration and is never guessed. <see cref="TryParseOrigin"/> accepts only an absolute http or
/// https URL with no path, query, fragment or user info; everything else is refused, and both callers refuse to
/// run rather than send a link nobody can open. That asymmetry is what OPEN_ITEMS 105 records about the recovery
/// link, whose template still defaults to a bare path.
/// </para>
/// </summary>
public static class AcceptanceLink
{
    /// <summary>The interface's path for accepting an invitation, below the origin.</summary>
    public const string Path = "/app/invitations/accept";

    public static string For(Uri origin, Guid tenantId, string token) =>
        $"{origin.GetLeftPart(UriPartial.Authority)}{Path}#tenant={tenantId:D}&token={token}";

    /// <summary>An origin and nothing else: scheme and authority, no path, query, fragment or user info.</summary>
    public static bool TryParseOrigin(string? value, out Uri origin)
    {
        origin = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")
            || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
            return false;
        origin = uri;
        return true;
    }
}

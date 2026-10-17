using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Core.Http;

/// <summary>
/// The session the stamp has ended (Amendment 01 §2.1, §5.2): the cookie's security_stamp is not the one on the
/// user's row — the password changed, here or in another session — or the cookie carries none at all. Mapped to
/// 401 session_expired. The cookie is not cleared from the browser: the next sign-in replaces it.
/// </summary>
public sealed class SessionExpiredException()
    : InvalidOperationException("The session's security stamp is not the user's current one (Amendment 01 §2.1).");

/// <summary>
/// The comparison of Amendment 01 §2.1: one read of the user's own row, under the existing user_self_read policy,
/// in the transaction of the request. A password change writes a new stamp, so every cookie minted before it —
/// in this browser or any other — fails here on its next request. It closes OPEN_ITEMS 36 only in part: logging
/// out does not change the stamp, so a copied cookie still survives a logout until it expires.
/// </summary>
public static class SessionStamp
{
    /// <summary>
    /// Throws <see cref="SessionExpiredException"/> unless <paramref name="cookieStamp"/> is the stamp on the
    /// user's row. Called inside the request's transaction, with app.user_id set: the policy decides which row
    /// is readable, the code never filters by user.
    /// </summary>
    public static async Task VerifyAsync(CoreDbContext db, Guid userId, Guid? cookieStamp, CancellationToken ct)
    {
        if (cookieStamp is not { } stamp)
            throw new SessionExpiredException();
        var current = await db.Users.Where(u => u.Id == userId).Select(u => (Guid?)u.SecurityStamp).SingleOrDefaultAsync(ct);
        if (current != stamp)
            throw new SessionExpiredException();
    }
}

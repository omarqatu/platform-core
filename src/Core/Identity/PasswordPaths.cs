using System.Security.Cryptography;
using System.Text;
using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Core.Identity;

/// <summary>A recovery token refused: unknown, already used, or expired — one refusal for all three (§5.1).</summary>
public sealed class PasswordResetRefusedException(string reason) : InvalidOperationException(reason);

/// <summary>The recovery token (Amendment 01 §2.2): random, delivered to its owner; only its hash is stored.</summary>
public static class RecoveryToken
{
    public static string New() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

/// <summary>
/// The two halves of password recovery and change (Amendment 01 §5.1), each its own unit of work and its own role:
/// <list type="number">
/// <item><b>authenticator</b> verifies — the current password, or the recovery token it consumes — with no user
/// context and no tenant context.</item>
/// <item><b>app_user</b>, in the context of that one user and no tenant, writes the new hash through
/// <c>password_self_update</c> and a new <c>security_stamp</c> through <c>user_self_update</c>.</item>
/// </list>
/// Neither write is audited: <c>user_password_credentials</c> is never audited (v1.16 §7), and
/// <c>security_stamp</c> is on the exception list of §2.1 — so a password change leaves no trace until the
/// identity audit log exists (OPEN_ITEMS 52).
/// </summary>
public static class PasswordPaths
{
    /// <summary>
    /// UoW-1 of <c>POST /api/auth/password/reset</c>: the token, by its hash, verified and consumed in one
    /// authenticator transaction. Returns the owner of the consumed token — the one value that becomes
    /// <c>app.user_id</c> in UoW-2 (the named exception to §4.3-b, §0): it comes from the row the database
    /// handed us, never from the request.
    /// </summary>
    public static Task<Guid> ConsumeResetTokenAsync(
        AuthenticatorDbContext db, string token, CancellationToken cancellationToken = default) =>
        UnitOfWork.RunAsync(db, SessionContext.None, async (c, ct) =>
        {
            var hash = RecoveryToken.Hash(token);
            // The policies give this role every row; the match is by the hash in the query (§4.1).
            var row = await c.PasswordResetTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct)
                ?? throw new PasswordResetRefusedException("no recovery token for this value");
            if (row.UsedAt is not null)
                throw new PasswordResetRefusedException("the recovery token is already used");
            if (row.ExpiresAt <= DateTime.UtcNow)
                throw new PasswordResetRefusedException("the recovery token has expired");

            // Consumption is the only write (password_reset_tokens_authenticator_use). used_at is a concurrency
            // token, so a second consumption racing this one affects zero rows and is loud (3.5/5).
            row.UsedAt = DateTime.UtcNow;
            await CriticalWrite.SaveAsync(c, "password_reset_tokens consume", ct);
            return row.UserId;
        }, cancellationToken);

    /// <summary>
    /// UoW-1 of <c>PUT /api/me/password</c>: the current password, for the session's own user. Writes one
    /// auth_attempts row, as login does, so the limiter of §5.2 counts it too.
    /// </summary>
    public static async Task<bool> VerifyCurrentPasswordAsync(
        AuthenticatorDbContext db, Guid userId, string password, string? ipAddress,
        CancellationToken cancellationToken = default) =>
        await UnitOfWork.RunAsync(db, SessionContext.None, async (c, ct) =>
        {
            var now = DateTime.UtcNow;
            var account = await (
                from u in c.Users
                where u.Id == userId
                join credential in c.UserPasswordCredentials on u.Id equals credential.UserId into credentials
                from credential in credentials.DefaultIfEmpty()
                select new { u.Username, u.Status, Hash = credential == null ? null : credential.PasswordHash })
                .SingleOrDefaultAsync(ct);

            var verified = PasswordHashing.Verify(password, account?.Hash ?? PasswordHashing.DummyHash);
            var succeeded = verified && account is { Status: "active", Hash: not null };

            Authenticator.Record(c, account?.Username ?? userId.ToString("D"), ipAddress, succeeded, now);
            await c.SaveChangesAsync(ct);
            return succeeded;
        }, cancellationToken);

    /// <summary>
    /// UoW-2 of both paths: the new hash and a new stamp, as app_user, in the context of that user alone — no
    /// tenant, so nothing of a tenant's is reachable in it. Both are critical writes: a row this context cannot
    /// see updates zero rows, which is an error, never a silent success (3.5/5). Returns the new stamp, for the
    /// cookie the change-password path re-issues; the reset path issues none (§5.1).
    /// </summary>
    public static Task<Guid> ApplyNewPasswordAsync(
        CoreDbContext db, Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        // The derivation is 210,000 PBKDF2 iterations: it happens before the transaction opens, never with one
        // held open (4.2, 3.5/1).
        var hash = PasswordHashing.Hash(newPassword);
        return UnitOfWork.RunAsync(db, new SessionContext(userId, null), async (c, ct) =>
        {
            var now = DateTime.UtcNow;
            var stamp = Guid.CreateVersion7();

            // app_user has UPDATE on user_password_credentials and no SELECT (§3.8): the row is written through
            // the change tracker without being read, as a stub attached with its key.
            var credential = new UserPasswordCredential { UserId = userId };
            c.Attach(credential);
            credential.PasswordHash = hash;
            credential.UpdatedAt = now;

            var user = CriticalWrite.Require(
                await c.Users.SingleOrDefaultAsync(u => u.Id == userId, ct), "users.security_stamp");
            user.SecurityStamp = stamp;

            await CriticalWrite.SaveAsync(c, "password change", ct);
            return stamp;
        }, cancellationToken);
    }
}

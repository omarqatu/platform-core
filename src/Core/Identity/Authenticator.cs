using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Core.Identity;

/// <summary>
/// A Core context on the authenticator connection (4.3-a/1): its own data source and role, the provisioner
/// pattern. Same model and transaction layer as the app_user context; a different surface.
/// </summary>
public sealed class AuthenticatorDbContext(DbContextOptions<AuthenticatorDbContext> options) : CoreDbContext(options);

/// <summary>How an attempt ended (Amendment 01 §5.2): refused by the ip ceiling, before the password was read.</summary>
public enum AuthOutcome
{
    Succeeded,
    Failed,
    RefusedTooManyAttempts,
}

public sealed record AuthResult(AuthOutcome Outcome, Guid? UserId);

/// <summary>
/// The login path's first half (4.3-a): resolve the credential and write the attempt, as authenticator, with no
/// tenant context and no user context. Its writes are auth_attempts and — since Amendment 01 §2.2, §3 —
/// password_reset_tokens; it writes nothing to the identity tables themselves (4.3-a/4, as amended by §0).
/// </summary>
public static class Authenticator
{
    /// <summary>
    /// The user's id when the credential is valid and the account active; otherwise null. Every attempt — an
    /// existing or a missing username, a right or a wrong password — runs the same query, the same hash work,
    /// and writes one auth_attempts row (T3.5).
    /// <para>
    /// The limiter of Amendment 01 §5.2 wraps it, in the direction of OPEN_ITEMS 16 and no other: the window's
    /// failures from this ip at or above the ceiling → refused before the password is read (429), and otherwise
    /// an escalating delay from the window's failures for this (username + ip), with no account lockout. The
    /// delay is awaited after the transaction has committed — never with one open — and only after a failure, so
    /// a correct password is never slowed. The refusal depends on the ip alone, so it says nothing about which
    /// usernames exist.
    /// </para>
    /// </summary>
    public static async Task<AuthResult> AuthenticateAsync(
        AuthenticatorDbContext db, string username, string password, string? ipAddress, LoginLimits limits,
        CancellationToken cancellationToken = default)
    {
        var (result, delay) = await UnitOfWork.RunAsync(db, SessionContext.None, async (c, ct) =>
        {
            var now = DateTime.UtcNow;
            var since = now - limits.Window;

            if (ipAddress is { } ip && limits.PerIpCeiling > 0
                && await FailuresAsync(c, username: null, ip, since, ct) >= limits.PerIpCeiling)
            {
                // Before the password is read: no credential query, no hash work for this request.
                Record(c, username, ipAddress, succeeded: false, now);
                await c.SaveChangesAsync(ct);
                return (new AuthResult(AuthOutcome.RefusedTooManyAttempts, null), TimeSpan.Zero);
            }

            var failures = await FailuresAsync(c, username, ipAddress, since, ct);

            var account = await (
                from u in c.Users
                where u.Username == username
                join credential in c.UserPasswordCredentials on u.Id equals credential.UserId into credentials
                from credential in credentials.DefaultIfEmpty()
                select new { u.Id, u.Status, Hash = credential == null ? null : credential.PasswordHash })
                .SingleOrDefaultAsync(ct);

            var verified = PasswordHashing.Verify(password, account?.Hash ?? PasswordHashing.DummyHash);
            var succeeded = verified && account is { Status: "active", Hash: not null };

            Record(c, username, ipAddress, succeeded, now);
            await c.SaveChangesAsync(ct);

            return succeeded
                ? (new AuthResult(AuthOutcome.Succeeded, account!.Id), TimeSpan.Zero)
                : (new AuthResult(AuthOutcome.Failed, null), limits.DelayFor(failures));
        }, cancellationToken);

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
        return result;
    }

    /// <summary>
    /// The window's failed attempts for an ip, and for one username on that ip when <paramref name="username"/>
    /// is given. An attempt with no recorded address counts only towards its username.
    /// </summary>
    internal static Task<int> FailuresAsync(
        CoreDbContext db, string? username, string? ip, DateTime since, CancellationToken ct)
    {
        var attempts = db.AuthAttempts.Where(a => !a.Succeeded && a.CreatedAt >= since);
        if (username is not null)
            attempts = attempts.Where(a => a.UsernameEntered == username);
        attempts = ip is null
            ? attempts.Where(a => a.IpAddress == null)
            : attempts.Where(a => a.IpAddress == ip);
        return attempts.CountAsync(ct);
    }

    /// <summary>One auth_attempts row per attempt — this role's own log, outside automatic auditing (7).</summary>
    internal static void Record(CoreDbContext db, string username, string? ipAddress, bool succeeded, DateTime now) =>
        db.AuthAttempts.Add(new AuthAttempt
        {
            Id = Guid.CreateVersion7(),
            UsernameEntered = username,
            IpAddress = ipAddress,
            Succeeded = succeeded,
            CreatedAt = now,
        });
}

using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Core.Identity;

/// <summary>
/// A Core context on the authenticator connection (4.3-a/1): its own data source and role, the provisioner
/// pattern. Same model and transaction layer as the app_user context; a different surface.
/// </summary>
public sealed class AuthenticatorDbContext(DbContextOptions<AuthenticatorDbContext> options) : CoreDbContext(options);

/// <summary>
/// The login path's first half (4.3-a): resolve the credential and write the attempt, as authenticator, with no
/// tenant context and no user context. auth_attempts is this role's only write (4.3-a/2); it writes nothing to
/// the identity tables (4.3-a/4).
/// </summary>
public static class Authenticator
{
    /// <summary>
    /// The user's id when the credential is valid and the account active; otherwise null. Every attempt — an
    /// existing or a missing username, a right or a wrong password — runs the same query, the same hash work,
    /// and writes one auth_attempts row (T3.5).
    /// </summary>
    public static Task<Guid?> AuthenticateAsync(
        AuthenticatorDbContext db, string username, string password, string? ipAddress, CancellationToken cancellationToken = default) =>
        UnitOfWork.RunAsync(db, SessionContext.None, async (c, ct) =>
        {
            var account = await (
                from u in c.Users
                where u.Username == username
                join credential in c.UserPasswordCredentials on u.Id equals credential.UserId into credentials
                from credential in credentials.DefaultIfEmpty()
                select new { u.Id, u.Status, Hash = credential == null ? null : credential.PasswordHash })
                .SingleOrDefaultAsync(ct);

            var verified = PasswordHashing.Verify(password, account?.Hash ?? PasswordHashing.DummyHash);
            var succeeded = verified && account is { Status: "active", Hash: not null };

            c.AuthAttempts.Add(new AuthAttempt
            {
                Id = Guid.CreateVersion7(),
                UsernameEntered = username,
                IpAddress = ipAddress,
                Succeeded = succeeded,
                CreatedAt = DateTime.UtcNow,
            });
            await c.SaveChangesAsync(ct);

            return succeeded ? account!.Id : (Guid?)null;
        }, cancellationToken);
}

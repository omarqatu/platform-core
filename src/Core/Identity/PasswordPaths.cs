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
    /// What <c>POST /api/auth/password/forgot</c> did, for the endpoint that answers 204 either way.
    /// <c>Mail</c> is null when there is nothing to send — the address has no single active account,
    /// or the request was refused by the ip ceiling. <c>Delay</c> is the limiter's escalating wait,
    /// which the endpoint applies <b>after</b> the transaction and identically in every case (§5.2).
    /// </summary>
    public sealed record ForgotPasswordOutcome(bool RefusedTooManyAttempts, RecoveryMail? Mail, TimeSpan Delay);

    /// <summary>The address and the token to send it, as the request gave the address (never normalized away).</summary>
    public sealed record RecoveryMail(string Address, string Token, DateTime ExpiresAt);

    /// <summary>
    /// <c>POST /api/auth/password/forgot</c> (Amendment 01 §5.1): one <c>authenticator</c> unit of work, keyed by
    /// the address, with no session of any kind.
    /// <list type="number">
    /// <item>The ip ceiling of §5.2, before anything is looked up — it depends on the address's ip alone, so it
    /// says nothing about which addresses have accounts.</item>
    /// <item>One <c>auth_attempts</c> row for <b>every</b> request, found or not: the escalating delay is then a
    /// function of (address + ip) alone, so the limiter cannot become the oracle the uniform 204 prevents.</item>
    /// <item>The lookup: the address, normalized by the rule of <see cref="EmailAddress"/> — the expression of
    /// <c>persons_email_normalized_key</c> — to the one <b>active</b> user of that person.</item>
    /// <item>Every outstanding token of that user is consumed, so the newest request is the only one that works
    /// (the roadmap's rule for this path, M2/c), and the new one is inserted.</item>
    /// </list>
    /// <para>
    /// <b>The lookup is raw SQL, and that is the grant's doing.</b> Amendment 01 §3 grants this role
    /// <c>SELECT (id, email)</c> on <c>persons</c> and nothing else; PostgreSQL refuses any statement that reads
    /// another column of the table — <c>SELECT *</c>, or a <c>WHERE</c> over <c>full_name</c> — with 42501, proven
    /// in <c>docs/proposals/a3-forgot-pg-proof</c> (cases 2c–2e). A tracked EF query over <c>persons</c> reads
    /// every mapped column, so it cannot be used here at all; a projection would emit the right columns, but what
    /// EF emits is not something this file can state. The statement is therefore written out, which also pins it
    /// to the normalized unique index. It is a read: no audit and no rows-affected guard apply to it.
    /// </para>
    /// <para>
    /// <b>Nothing is sent from inside this transaction</b> (§5.5), and nothing here waits on a provider: the
    /// caller hands the returned <c>Mail</c> to the dispatcher, which is what lets the
    /// response time stop depending on whether the account exists (OPEN_ITEMS 51).
    /// </para>
    /// </summary>
    public static async Task<ForgotPasswordOutcome> BeginResetAsync(
        AuthenticatorDbContext db, string email, string? ipAddress, LoginLimits limits, TimeSpan tokenLifetime,
        CancellationToken cancellationToken = default)
    {
        var normalized = EmailAddress.Normalize(email);
        return await UnitOfWork.RunAsync(db, SessionContext.None, async (c, ct) =>
        {
            var now = DateTime.UtcNow;
            var since = now - limits.Window;

            if (ipAddress is { } ip && limits.PerIpCeiling > 0
                && await Authenticator.FailuresAsync(c, username: null, ip, since, ct) >= limits.PerIpCeiling)
            {
                Authenticator.Record(c, normalized, ipAddress, succeeded: false, now);
                await c.SaveChangesAsync(ct);
                return new ForgotPasswordOutcome(true, null, TimeSpan.Zero);
            }

            var failures = await Authenticator.FailuresAsync(c, normalized, ipAddress, since, ct);
            // Recorded before the lookup and whatever the lookup finds: a request for an address with an account
            // and one for an address without must leave the same trace and earn the same next delay.
            Authenticator.Record(c, normalized, ipAddress, succeeded: false, now);

            var owners = await c.Database.SqlQuery<Guid>(
                $"""
                 SELECT u.id AS "Value"
                   FROM persons p
                   JOIN users u ON u.person_id = p.id
                  WHERE lower(btrim(p.email)) = {normalized}
                    AND u.status = 'active'
                 """).ToListAsync(ct);

            // One address, one account. Two active accounts behind one address is possible in the schema
            // (users.person_id is not unique, and a person can be both 'employee' and 'customer'), and neither
            // v1.16 nor the amendment says which of them recovery means — so nothing is sent, and the answer is
            // the same 204 as for an unknown address (OPEN_ITEMS 57, for the project owner).
            if (owners is not [var owner])
            {
                await c.SaveChangesAsync(ct);
                return new ForgotPasswordOutcome(false, null, limits.DelayFor(failures));
            }

            // The newest request is the only live one: the outstanding tokens are consumed, not deleted — no
            // application role holds DELETE on this table, and password_reset_tokens_authenticator_use admits
            // exactly this transition (USING used_at IS NULL, WITH CHECK used_at IS NOT NULL).
            //
            // It is one statement rather than tracked entities, and that is not a style choice. used_at is a
            // concurrency token (§2.2), so a tracked update of a row another request consumed meanwhile affects
            // zero rows and EF raises — which this path must not do: two forgot requests racing on one address
            // would then answer 500 where an address with no account answers 204, and the response would be
            // telling them apart again (OPEN_ITEMS 51). The statement below is idempotent instead: it consumes
            // whatever is still live and affects zero rows if another request got there first, which is the
            // state it wanted anyway. No rows-affected guard, because no particular number is required; the
            // write that must land is the insert below, and it is guarded.
            // The WHERE reads user_id and used_at, which this role holds table-level SELECT on, and the SET
            // writes used_at alone, which is its column grant — run both ways in docs/proposals/a3-forgot-pg-proof.
            await c.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE password_reset_tokens SET used_at = {now} WHERE user_id = {owner} AND used_at IS NULL", ct);

            var token = RecoveryToken.New();
            var expiresAt = now + tokenLifetime;
            c.PasswordResetTokens.Add(new PasswordResetToken
            {
                Id = Guid.CreateVersion7(),
                UserId = owner,
                TokenHash = RecoveryToken.Hash(token),
                RequestedAt = now,
                ExpiresAt = expiresAt,
                RequestIp = ipAddress,
            });

            // The attempt row and the new token land together: the token exists only if the request that
            // asked for it was recorded. A critical write (3.5/5) — a row this role's policies refuse must be
            // an error, never a 204 with nothing behind it.
            await CriticalWrite.SaveAsync(c, "password_reset_tokens request", ct);
            return new ForgotPasswordOutcome(false, new RecoveryMail(email, token, expiresAt), limits.DelayFor(failures));
        }, cancellationToken);
    }

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

            // app_user may UPDATE password_hash and updated_at on user_password_credentials and holds no SELECT
            // on that table at all (§3.8; 1.11 forbids even a column SELECT on it). PostgreSQL requires SELECT
            // on every column a statement *reads*, so an UPDATE carrying `WHERE user_id = …` is refused with
            // 42501 — and EF always writes that WHERE. The statement therefore carries no WHERE at all:
            // password_self_update (user_id = app.user_id) picks the one row, which is the rule of this
            // architecture applied to the user axis — the code never filters what RLS filters. Raw SQL is the
            // only way to send it, as checks/local/bulk-writes-allowlist.txt already records ("the password
            // command … is a raw SQL command, not an EF bulk command"); the table is never audited (7), so the
            // automatic audit loses nothing by not seeing it.
            //
            // A statement with no WHERE writes every row the policy admits, so the count is the guard: EXACTLY
            // one row, never merely "not zero" (3.5/5). Today password_self_update admits one; if it were ever
            // widened, this throws on the second row and the transaction rolls back, so no widening of that
            // policy can turn this into a silent mass password change. The two Conformance tests
            // (T3_PasswordTests: the caller's row alone, and nothing at all with no app.user_id) prove the
            // isolation is the policy's doing and not an accident of the data.
            await CriticalWrite.ExpectRowsAsync(
                c.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE user_password_credentials SET password_hash = {hash}, updated_at = {now}", ct),
                1, "user_password_credentials password change");

            // The stamp is a tracked write on the user's own row (user_self_update), exempt from auditing by
            // Amendment 01 §2.1 — app_user does hold SELECT on users, so EF's WHERE on the key is readable here.
            var user = CriticalWrite.Require(
                await c.Users.SingleOrDefaultAsync(u => u.Id == userId, ct), "users.security_stamp");
            user.SecurityStamp = stamp;

            await CriticalWrite.SaveAsync(c, "users.security_stamp", ct);
            return stamp;
        }, cancellationToken);
    }
}

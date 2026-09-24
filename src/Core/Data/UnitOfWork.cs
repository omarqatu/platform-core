using Microsoft.EntityFrameworkCore;

namespace Core.Data;

/// <summary>
/// One explicit transaction, with the context variables set first (3.5/1).
/// The request middleware and, later, background jobs (8) both run through here.
/// </summary>
public static class UnitOfWork
{
    public static async Task<T> RunAsync<TContext, T>(
        TContext db, SessionContext session, Func<TContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
        where TContext : CoreDbContext
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await ApplyContextAsync(db, session, cancellationToken);
            var result = await work(db, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            db.Scope = null;
        }
    }

    public static Task RunAsync<TContext>(
        TContext db, SessionContext session, Func<TContext, CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
        where TContext : CoreDbContext =>
        RunAsync<TContext, bool>(db, session, async (context, ct) =>
        {
            await work(context, ct);
            return true;
        }, cancellationToken);

    // Each variable is its own SET LOCAL statement (3.5/2), in the order app.user_id, app.tenant_id,
    // then — when both a user and an active tenant are present — the three second-axis variables,
    // resolved from the database on every transaction in the order of Rule 6 (3.5/1, 3.5/6).
    // A tenant with no user (no membership to resolve) sets no second-axis variable: the policies
    // beneath fail safe into zero rows on the second axis.
    private static async Task ApplyContextAsync(CoreDbContext db, SessionContext session, CancellationToken ct)
    {
        if (session.UserId is { } userId)
            await SetLocalAsync(db, "app.user_id", userId, ct);
        if (session.TenantId is { } tenantId)
            await SetLocalAsync(db, "app.tenant_id", tenantId, ct);
        if (session is { UserId: { } user, TenantId: { } tenant })
            db.Scope = await ScopeResolver.ResolveAsync(db, user, tenant, ct);
    }

    // SET LOCAL takes no bind parameters. The variable name is one of two
    // constants above, and the value a Guid rendered in "D" format (hex and
    // hyphens only), so nothing caller-controlled reaches the SQL text.
    private static Task SetLocalAsync(CoreDbContext db, string variable, Guid value, CancellationToken ct) =>
#pragma warning disable EF1003
        db.Database.ExecuteSqlRawAsync("SET LOCAL " + variable + " = '" + value.ToString("D") + "'", ct);
#pragma warning restore EF1003
}

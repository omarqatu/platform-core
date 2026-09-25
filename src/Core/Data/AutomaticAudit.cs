using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Core.Data;

/// <summary>
/// Who and where the audit entries of one transaction belong to (PLATFORM_CORE 7): the tenant the transaction's
/// app.tenant_id names — the only tenant_id the audit_log write policies accept — and the actor. Set by the unit
/// of work, and by the provisioner paths once they set app.tenant_id (4.4/4); cleared with the transaction.
/// </summary>
public sealed record AuditContext(Guid? TenantId, Guid? ActorId, string ActorType, string? IpAddress);

/// <summary>A write that must be audited has no tenant to audit it under: loud, never an unaudited write (7).</summary>
public sealed class AuditContextMissingException(string entityType)
    : InvalidOperationException(
        $"A write to {entityType} has no audit context with a tenant: every write is audited in its transaction (PLATFORM_CORE 7).")
{
    public string EntityType { get; } = entityType;
}

/// <summary>
/// Automatic auditing (PLATFORM_CORE 7, 1.16): every tracked write is audited in its own transaction. The changes
/// are captured at SavingChanges; the audit entries are written by a second save from SavedChanges — in the same
/// transaction, rolled back with it — under a flag that keeps this interceptor from auditing the audit save.
/// <list type="bullet">
/// <item>The exemption list: self-service identity writes — users (last_login_at, language, theme) on the actor's
/// own row, via user_self_update — because audit_log is a tenant log and these are not tenant data;
/// user_password_credentials is never audited; auth_attempts is its own log.</item>
/// <item>The masking list: password_hash and token_hash stay in old_value/new_value with the value "[masked]" —
/// the entry shows the column was written without revealing it.</item>
/// </list>
/// Bulk commands (ExecuteUpdate, ExecuteDelete) bypass the change tracker and so this interceptor; a local static
/// check forbids them in application code (checks/local/check-no-bulk-writes.sh).
/// </summary>
public sealed class AutomaticAuditInterceptor : SaveChangesInterceptor
{
    public static readonly AutomaticAuditInterceptor Instance = new();

    public const string Masked = "[masked]";
    internal static readonly HashSet<string> MaskedColumns = ["password_hash", "token_hash"];
    private static readonly HashSet<string> SelfServiceUserProperties =
        [nameof(User.LastLoginAt), nameof(User.Language), nameof(User.Theme)];

    private AutomaticAuditInterceptor()
    {
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (TakeEntries(eventData.Context) is { } db)
            SaveAudit(db);
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (TakeEntries(eventData.Context) is { } db)
            await SaveAuditAsync(db, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context is CoreDbContext db && !db.SavingAudit)
            db.PendingAudit = null;
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        SaveChangesFailed(eventData);
        return Task.CompletedTask;
    }

    private static void Capture(DbContext? context)
    {
        if (context is not CoreDbContext db || db.SavingAudit)
            return;
        db.PendingAudit = null;

        db.ChangeTracker.DetectChanges();
        var audit = db.Audit;
        var now = DateTime.UtcNow;
        var entries = new List<AuditEntry>();
        foreach (var entry in db.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted) || Exempt(entry, audit))
                continue;
            var entityType = entry.Metadata.GetTableName()!;
            // With no transaction, the command itself fails loudly beneath (TransactionRequiredInterceptor, 3.5).
            if (db.Database.CurrentTransaction is null)
                return;
            if (audit?.TenantId is not { } tenant)
                throw new AuditContextMissingException(entityType);

            entries.Add(new AuditEntry
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                ActorId = audit.ActorId,
                ActorType = audit.ActorType,
                Action = entry.State switch
                {
                    EntityState.Added => "insert",
                    EntityState.Modified => "update",
                    _ => "delete",
                },
                EntityType = entityType,
                EntityId = entry.Metadata.FindPrimaryKey() is { Properties: [{ ClrType: var keyType } key] } && keyType == typeof(Guid)
                    ? (Guid)entry.Property(key.Name).CurrentValue!
                    : null,
                OldValue = entry.State is EntityState.Added ? null : Values(entry, original: true),
                NewValue = entry.State is EntityState.Deleted ? null : Values(entry, original: false),
                IpAddress = audit.IpAddress,
                CreatedAt = now,
            });
        }
        db.PendingAudit = entries.Count > 0 ? entries : null;
    }

    private static bool Exempt(EntityEntry entry, AuditContext? audit) => entry.Entity switch
    {
        AuditEntry or UserPasswordCredential or AuthAttempt => true,
        User user => entry.State == EntityState.Modified
                     && audit?.ActorId == user.Id
                     && entry.Properties.Where(p => p.IsModified).All(p => SelfServiceUserProperties.Contains(p.Metadata.Name)),
        _ => false,
    };

    // Added and Deleted: every column. Modified: the columns written, beside the key — old and new alike.
    private static string Values(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var property in entry.Properties)
        {
            if (entry.State == EntityState.Modified && !property.IsModified && !property.Metadata.IsPrimaryKey())
                continue;
            var column = property.Metadata.GetColumnName();
            values[column] = MaskedColumns.Contains(column)
                ? Masked
                : original ? property.OriginalValue : property.CurrentValue;
        }
        return JsonSerializer.Serialize(values);
    }

    private static CoreDbContext? TakeEntries(DbContext? context) =>
        context is CoreDbContext { SavingAudit: false, PendingAudit: not null } db ? db : null;

    private static void SaveAudit(CoreDbContext db)
    {
        db.SavingAudit = true;
        try
        {
            db.AuditLog.AddRange(db.PendingAudit!);
            db.PendingAudit = null;
            db.SaveChanges();
        }
        finally
        {
            db.SavingAudit = false;
        }
    }

    private static async Task SaveAuditAsync(CoreDbContext db, CancellationToken cancellationToken)
    {
        db.SavingAudit = true;
        try
        {
            db.AuditLog.AddRange(db.PendingAudit!);
            db.PendingAudit = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.SavingAudit = false;
        }
    }
}

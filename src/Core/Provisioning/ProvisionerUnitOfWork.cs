using Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Core.Provisioning;

/// <summary>
/// A Core context on the provisioner connection (4.4/2): its own data source and role — never SET ROLE on an
/// app_user connection. Same model, transaction layer and automatic audit; a different surface.
/// </summary>
public sealed class ProvisionerDbContext(DbContextOptions<ProvisionerDbContext> options) : CoreDbContext(options);

/// <summary>
/// The provisioner's transaction (4.4/3): each of its two paths — bootstrap and acceptance — is one transaction,
/// and its auditing is in the same transaction (4.4/4). The path names its tenant with <see cref="EnterTenantAsync"/>
/// before its first read or write of tenant data (3.9: app.tenant_id first).
/// </summary>
public static class ProvisionerUnitOfWork
{
    public static async Task<T> RunAsync<T>(ProvisionerDbContext db, Func<ProvisionerDbContext, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var result = await work(db, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            db.Audit = null;
        }
    }

    /// <summary>
    /// SET LOCAL app.tenant_id — the one variable the provisioner policies read — then the audit context of this
    /// path in that tenant (7): the audit_log_provisioner_insert policy accepts that tenant_id alone.
    /// </summary>
    public static async Task EnterTenantAsync(CoreDbContext db, Guid tenantId, Guid? actorId, string actorType,
        CancellationToken cancellationToken)
    {
        // SET LOCAL takes no bind parameters: a constant name and a Guid in "D" format (hex and hyphens only).
#pragma warning disable EF1003
        await db.Database.ExecuteSqlRawAsync("SET LOCAL app.tenant_id = '" + tenantId.ToString("D") + "'", cancellationToken);
#pragma warning restore EF1003
        db.Audit = new AuditContext(tenantId, actorId, actorType, db.ClientAddress);
    }
}

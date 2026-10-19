using Core.Data;

namespace Core.Provisioning;

public sealed record CreateTenantRequest(string TenantName, string OwnerEmail);

/// <summary>The new tenant, and the bootstrap invitation's token — returned once, stored nowhere but as its hash.</summary>
public sealed record CreateTenantResult(Guid TenantId, Guid InvitationId, string Token, DateTime ExpiresAt);

/// <summary>
/// The operations procedure's create-tenant (Amendment 01 §5.4, D2): the provisioner's third path — it amends v1.16
/// §4.4/3 and §4.4/5 — in one transaction, audited in it (§4.4/4). A tenant from the templates, then an `owner`
/// invitation with no inviter (§2.11, invitations_provisioner_insert), scope 'all' (OPEN_ITEMS 24).
/// <para>
/// No person, user or membership: the owner accepts in person through the existing acceptance path, which creates
/// them — the consent principle (§0) with no exception for bootstrap. Until then the tenant has no active owner (§0,
/// amending §3.10 item a).
/// </para>
/// <para>
/// <b>§5.4 in part (OPEN_ITEMS 89):</b> the platform_subscriptions row and the default tenant_notification_settings
/// are not written, because neither table exists yet; the migrations that create them backfill this tenant.
/// </para>
/// </summary>
public static class CreateTenant
{
    public static Task<CreateTenantResult> RunAsync(ProvisionerDbContext db, CreateTenantRequest request, CancellationToken cancellationToken = default) =>
        ProvisionerUnitOfWork.RunAsync(db, (c, ct) => InTransactionAsync(c, request, ct), cancellationToken);

    /// <summary>The path itself, inside a transaction the caller opened (the white-box tests reach it this way).</summary>
    public static async Task<CreateTenantResult> InTransactionAsync(CoreDbContext db, CreateTenantRequest request, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var tenantId = Guid.CreateVersion7();
        // actor_type as Bootstrap writes it: the same role on the same kind of path, so the audit log reads one way.
        await ProvisionerUnitOfWork.EnterTenantAsync(db, tenantId, null, "provisioner", ct);

        var ownerRole = await Bootstrap.TenantFromTemplatesAsync(db, tenantId, request.TenantName, now, ct);

        var token = InvitationToken.New();
        var invitation = new Invitation
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Email = request.OwnerEmail, RoleId = ownerRole,
            TokenHash = InvitationToken.Hash(token), Status = "pending", InvitedBy = null,
            ExpiresAt = now + InvitationToken.Lifetime, CreatedAt = now, IntendedScopeMode = "all",
        };
        db.Invitations.Add(invitation);
        await CriticalWrite.SaveAsync(db, "invitations insert (bootstrap)", ct);

        return new CreateTenantResult(tenantId, invitation.Id, token, invitation.ExpiresAt);
    }
}

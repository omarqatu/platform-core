namespace Core.Provisioning;

/// <summary>
/// <c>POST /api/members/invitations</c> (PLATFORM_CORE §4.5; OPEN_ITEMS 60, 106), bound from <c>Invitations</c>.
/// <para>
/// Both limits are marked assumptions <c>[—]</c> until the project owner settles them at launch, as the login
/// limiter's numbers are (OPEN_ITEMS 16, 54).
/// </para>
/// </summary>
public sealed class InvitationSettings
{
    /// <summary>
    /// The platform's own origin, for the acceptance link the e-mail carries. **There is no default, and Api
    /// refuses to start without a valid one** — the same guard `Ops create-tenant` has, and for the same reason:
    /// a template that falls back to a bare path sends a link nobody can click, silently, and only the invitee
    /// finds out (OPEN_ITEMS 105, which records that the recovery link still works that way).
    /// </summary>
    public string? PublicOrigin { get; set; }

    /// <summary>How many invitations one member may create in <see cref="PerInviterWindow"/>. Marked `[—]`.</summary>
    public int PerInviter { get; set; } = 10;

    public TimeSpan PerInviterWindow { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// How many invitations the whole organization may create in <see cref="PerTenantWindow"/>. Marked `[—]`.
    /// <para>
    /// Above the per-inviter limit and not instead of it: a per-inviter window multiplies by the number of
    /// inviters, while the provider's quota is per sending account. Production sends through one Gmail account
    /// whose daily allowance is shared by every tenant, so the ceiling protects the provider as much as the
    /// organization (the project owner's decision, 2026-10-21).
    /// </para>
    /// </summary>
    public int PerTenant { get; set; } = 30;

    public TimeSpan PerTenantWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>The origin as a URI, or a refusal the caller turns into a failure to start.</summary>
    public bool TryOrigin(out Uri origin) => AcceptanceLink.TryParseOrigin(PublicOrigin, out origin);
}

namespace Conformance;

// PLATFORM_CORE Amendment 01 §2.11, §3, §4.1 — the bootstrap invitation: an `owner` invitation with no human inviter
// (invited_by NULL), written by provisioner alone, for a tenant created by the operations procedure (§5.4). In SQL,
// as each role, on the seed tenants; every session rolls back.
//
// The argument that dropping NOT NULL from invited_by opens nothing to app_user is
// AppUser_CannotWriteANullInviter_EvenWithEveryVariableSet, beside its control: invitations_insert requires
// invited_by = app.user_id, and NULL = <anything> is NULL — not true — so WITH CHECK refuses the row. The only
// writer of a NULL inviter is invitations_provisioner_insert, which requires one.
public class T4_BootstrapInvitationTests
{
    private const string Insert =
        "INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) " +
        "VALUES (@id, @t, 'bootstrap-owner@b.test', @r, @h, @s, @u, now() + interval '7 days', now(), 'all')";

    private static async Task<(string, object)[]> RowAsync(string tenant, string status = "pending", Guid? invitedBy = null) =>
    [
        ("id", Guid.CreateVersion7()), ("t", await Seed.TenantAsync(tenant)), ("r", await Seed.RoleAsync(tenant, "owner")),
        ("h", Guid.CreateVersion7().ToString("N")), ("s", status), ("u", (object?)invitedBy ?? DBNull.Value),
    ];

    // ---- §2.11 + §4.1: provisioner writes a pending owner invitation with no inviter, in its own tenant — and reads
    // it back through invitations_provisioner_select, with no RETURNING.
    [Fact]
    public async Task Provisioner_WritesABootstrapInvitation_WithNoInviter()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        await using var session = await Session.OpenAsync(Target.Provisioner, tenant: alAmin);
        var row = await RowAsync(Seed.AlAmin);

        Assert.Equal(1, await session.ExecuteAsync(Insert, row));
        Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM invitations WHERE id = @id AND invited_by IS NULL", row[0]));
    }

    // ---- §4.1, invited_by IS NULL: the provisioner path forges no inviter.
    [Fact]
    public async Task Provisioner_CannotForgeAnInviter()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        await using var session = await Session.OpenAsync(Target.Provisioner, tenant: alAmin);

        Assert.Equal("42501", await session.SqlStateOfAsync(Insert, await RowAsync(Seed.AlAmin, invitedBy: await Seed.UserAsync("omar"))));
    }

    // ---- §4.1, status = 'pending': it writes no invitation already accepted (or revoked, or expired).
    [Theory]
    [InlineData("accepted")]
    [InlineData("revoked")]
    [InlineData("expired")]
    public async Task Provisioner_CannotWriteAnInvitationThatIsNotPending(string status)
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        await using var session = await Session.OpenAsync(Target.Provisioner, tenant: alAmin);

        Assert.Equal("42501", await session.SqlStateOfAsync(Insert, await RowAsync(Seed.AlAmin, status)));
    }

    // ---- §4.1, tenant_id = app.tenant_id: only into the tenant it named.
    [Fact]
    public async Task Provisioner_CannotWriteIntoAnotherTenant()
    {
        await using var session = await Session.OpenAsync(Target.Provisioner, tenant: await Seed.TenantAsync(Seed.Maan));

        Assert.Equal("42501", await session.SqlStateOfAsync(Insert, await RowAsync(Seed.AlAmin)));
    }

    // ---- §2.11: invited_by NULL through app_user is still refused — with every session variable set, as the owner.
    // Not because the column forbids it any more, but because invitations_insert's invited_by = app.user_id is NULL
    // for a NULL inviter, and a WITH CHECK that is not true refuses. The control below is the same session and the
    // same row with the owner as inviter: it is written, so the refusal is the NULL's and nothing else's.
    [Fact]
    public async Task AppUser_CannotWriteANullInviter_EvenWithEveryVariableSet()
    {
        var omar = await Seed.UserAsync("omar");
        await using var session = await OwnerSessionAsync(omar);

        Assert.Equal("42501", await session.SqlStateOfAsync(Insert, await RowAsync(Seed.AlAmin)));
        Assert.Equal(1, await session.ExecuteAsync(Insert, await RowAsync(Seed.AlAmin, invitedBy: omar)));
    }

    // ---- The column itself: NULL is now accepted by the schema (§2.11), so the refusals above are the policies'.
    [Fact]
    public async Task TheColumn_AcceptsNull()
    {
        Assert.Equal("YES", await Seed.ScalarStringAsMigratorAsync(
            "SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'invitations' AND column_name = 'invited_by'"));
    }

    private static async Task<Session> OwnerSessionAsync(Guid omar) =>
        await Session.OpenAsync(Target.AppUser, user: omar, tenant: await Seed.TenantAsync(Seed.AlAmin),
            membership: await Seed.MembershipAsync("omar", Seed.AlAmin), scopeAll: true, canManageScope: true, canManageMembers: true);
}

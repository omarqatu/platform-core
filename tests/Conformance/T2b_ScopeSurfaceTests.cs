namespace Conformance;

// PROOF_SPEC v1.2 T2b.3 — PLATFORM_CORE v1.15 §3.7 Tests 16-d, 17 and 24, the parts whose surfaces
// exist at T2b: the two tables of the second axis and their 4.8 policies, run as SQL under app_user
// against the seed contract, with no attribution columns (v1.15). The second-axis variables are set
// here the way T3's resolution will set them. Every session is rolled back.
// Not here: 17-c (a scope variable from a request — Check 9 statically, T3 through the API);
// 24-b's "sees no more than its own entities on the scoped tables" (T5, the first scoped table);
// 24-c (revoking the permission mid-session — T3).
public class T2b_ScopeSurfaceTests
{
    private static async Task<Session> AsMemberAsync(string username, bool scopeAll, bool canManageScope) =>
        await Session.OpenAsync(Target.AppUser,
            user: await Seed.UserAsync(username),
            tenant: await Seed.TenantAsync(Seed.AlAmin),
            membership: await Seed.MembershipAsync(username, Seed.AlAmin),
            scopeAll: scopeAll, canManageScope: canManageScope);

    private const string InsertAssignment =
        "INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active, reason) " +
        "VALUES (@id, @t, @m, @ref, 'contributor', true, 'conformance')";

    // ---- Test 16-d: an assigned member reads their own assignments and no other membership's;
    // their INSERT or UPDATE on scope_assignments fails.
    [Fact]
    public async Task Test16d_AssignedMember_ReadsOwnAssignmentsOnly_CannotWrite()
    {
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        var rami = await Seed.MembershipAsync("rami", Seed.AlAmin);
        await using var session = await AsMemberAsync("khaled", scopeAll: false, canManageScope: false);

        var visible = await session.ListAsync<Guid>("SELECT membership_id FROM scope_assignments");
        Assert.Equal(2, visible.Count);
        Assert.All(visible, m => Assert.Equal(khaled, m));
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM scope_assignments WHERE membership_id = @m", ("m", rami)));
        // The mode: their own row only.
        Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM membership_scope"));

        Assert.Equal("42501", await session.SqlStateOfAsync(InsertAssignment,
            ("id", Guid.CreateVersion7()), ("t", await Seed.TenantAsync(Seed.AlAmin)), ("m", khaled), ("ref", Guid.CreateVersion7())));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE scope_assignments SET active = false"));
        Assert.Equal(2, await session.CountAsync("SELECT count(*) FROM scope_assignments WHERE active"));
    }

    // ---- Test 17-a: app_user, even with an admin role, cannot UPDATE membership_scope for their own
    // membership; the same admin can for another member (the control).
    [Fact]
    public async Task Test17a_Admin_CannotRaiseOwnScope_CanChangeAnothers()
    {
        var sara = await Seed.MembershipAsync("sara", Seed.AlAmin);
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        await using var session = await AsMemberAsync("sara", scopeAll: true, canManageScope: true);

        Assert.Equal(0, await session.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'assigned' WHERE membership_id = @m", ("m", sara)));
        Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'all'", ("m", sara)));
        Assert.Equal(1, await session.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = @m", ("m", khaled)));
    }

    // ---- Test 17-b: the self-departure path has no column to raise the scope with — scope_mode is not on
    // memberships (a regression test on the 1.7 decision), and the only UPDATE grant on memberships is status.
    [Fact]
    public async Task Test17b_ScopeModeIsNotOnMemberships()
    {
        Assert.Equal(0, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND table_name = 'memberships' AND column_name = 'scope_mode'"));
        await using var session = await AsMemberAsync("khaled", scopeAll: false, canManageScope: false);
        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE memberships SET user_id = user_id"));
    }

    // ---- Test 24-a: a viewer with scope all and no core.scope.manage → INSERT or UPDATE on scope_assignments
    // and UPDATE on membership_scope fail from the database.
    [Fact]
    public async Task Test24a_ViewerWithScopeAll_CannotManageScope()
    {
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        await using var session = await AsMemberAsync("layla", scopeAll: true, canManageScope: false);

        Assert.Equal("42501", await session.SqlStateOfAsync(InsertAssignment,
            ("id", Guid.CreateVersion7()), ("t", await Seed.TenantAsync(Seed.AlAmin)), ("m", khaled), ("ref", Guid.CreateVersion7())));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE scope_assignments SET active = false"));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = @m", ("m", khaled)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'assigned'", ("m", khaled)));
    }

    // ---- Test 24-b (database part): a scope manager with scope assigned manages others' assignments, and
    // cannot raise their own mode (the "not the actor's own membership" condition).
    [Fact]
    public async Task Test24b_AssignedScopeManager_ManagesOthers_NotOwnMode()
    {
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        var rami = await Seed.MembershipAsync("rami", Seed.AlAmin);
        await using var session = await AsMemberAsync("rami", scopeAll: false, canManageScope: true);

        Assert.Equal(1, await session.ExecuteAsync(InsertAssignment,
            ("id", Guid.CreateVersion7()), ("t", await Seed.TenantAsync(Seed.AlAmin)), ("m", khaled), ("ref", Guid.CreateVersion7())));
        Assert.Equal(3, await session.ExecuteAsync("UPDATE scope_assignments SET active = false WHERE membership_id = @m", ("m", khaled)));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = @m", ("m", rami)));
        Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'assigned'", ("m", rami)));
    }
}

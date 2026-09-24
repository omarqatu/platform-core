namespace Conformance;

// PROOF_SPEC v1.1 T2.3 — PLATFORM_CORE v1.14 §3.7 Tests 1, 2, 6, 10, 12, 13, 15, 18, 21 — and T2.4
// (Test 23 via direct SQL as app_user). [B]: SQL under the real roles, against the seed contract (§7).
// Every session is rolled back. Test 8 is in T2_Test8.cs (it plants a policy and must run alone).
public class T2_DocumentTests
{
    // ---- Test 1: a query with no WHERE from Al-Amin's context → Al-Amin's rows only.
    [Fact]
    public async Task Test01_NoWhere_FromAlAmin_SeesAlAminRowsOnly()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var expectedRoles = await Seed.CountAsMigratorAsync("SELECT count(*) FROM roles WHERE tenant_id = @t", ("t", alAmin));
        var expectedMemberships = await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t", ("t", alAmin));
        await using var session = await Session.OpenAsync(Target.AppUser, tenant: alAmin);

        var roleTenants = await session.ListAsync<Guid>("SELECT tenant_id FROM roles");
        var membershipTenants = await session.ListAsync<Guid>("SELECT tenant_id FROM memberships");

        Assert.Equal(expectedRoles, roleTenants.Count);
        Assert.All(roleTenants, t => Assert.Equal(alAmin, t));
        Assert.Equal(expectedMemberships, membershipTenants.Count);
        Assert.All(membershipTenants, t => Assert.Equal(alAmin, t));
    }

    // ---- Test 2: an INSERT with Maan's tenant_id from Al-Amin's context → fails.
    [Fact]
    public async Task Test02_InsertWithMaansTenant_FromAlAmin_Fails()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var maan = await Seed.TenantAsync(Seed.Maan);
        await using var session = await Session.OpenAsync(Target.AppUser, user: await Seed.UserAsync("sara"), tenant: alAmin);

        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active) VALUES (@id, @t, 'custom', 'x', 'x', false, true)",
            ("id", Guid.CreateVersion7()), ("t", maan)));
        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO audit_log (id, tenant_id, actor_type, action, entity_type, created_at) VALUES (@id, @t, 'user', 'x', 'x', now())",
            ("id", Guid.CreateVersion7()), ("t", maan)));
    }

    // ---- Test 6: Al-Amin's admin queries persons → does not see the PII of Maan's users; sees every Al-Amin member.
    [Fact]
    public async Task Test06_AlAminAdmin_ReadsPersons_NoMaanPii_AllAlAminMembers()
    {
        await using var session = await Session.OpenAsync(Target.AppUser,
            user: await Seed.UserAsync("sara"), tenant: await Seed.TenantAsync(Seed.AlAmin));

        var emails = await session.ListAsync<string>("SELECT email FROM persons");

        Assert.DoesNotContain("nour@seed.test", emails);
        foreach (var member in new[] { "omar", "sara", "khaled", "layla", "rami" })
            Assert.Contains($"{member}@seed.test", emails);
    }

    // ---- Test 10: consent — app_user, even with an admin role, inserting into memberships → fails.
    [Fact]
    public async Task Test10_AdminInsertsMembershipDirectly_Fails()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        await using var session = await Session.OpenAsync(Target.AppUser, user: await Seed.UserAsync("sara"), tenant: alAmin,
            membership: await Seed.MembershipAsync("sara", Seed.AlAmin), scopeAll: true, canManageScope: true);

        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES (@id, @t, @u, 'active', now())",
            ("id", Guid.CreateVersion7()), ("t", alAmin), ("u", await Seed.UserAsync("nour"))));
    }

    // ---- Test 12: UPDATE/DELETE on audit_log from any application role → fails at the grant level.
    [Theory]
    [InlineData(Target.AppUser)]
    [InlineData(Target.Authenticator)]
    [InlineData(Target.JobRunner)]
    [InlineData(Target.Provisioner)]
    public async Task Test12_AuditLog_UpdateAndDelete_FailOnTheGrant(string role)
    {
        await using var session = await Session.OpenAsync(role, tenant: await Seed.TenantAsync(Seed.AlAmin), scopeAll: true);

        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE audit_log SET action = 'forged'"));
        Assert.Equal("42501", await session.SqlStateOfAsync("DELETE FROM audit_log"));
    }

    // ---- Test 13: hashes are hidden; the password change (no WHERE, no RETURNING) affects exactly one row,
    // and the WHERE form fails loudly (SQLSTATE 42501), not with zero rows (1.11, 1.12).
    [Fact]
    public async Task Test13_Credentials_NoSelect_OneRowUpdate_WhereFormLoud()
    {
        await using var session = await Session.OpenAsync(Target.AppUser, user: await Seed.UserAsync("omar"));

        Assert.Equal("42501", await session.SqlStateOfAsync("SELECT password_hash FROM user_password_credentials"));
        Assert.Equal(1, await session.ExecuteAsync(
            "UPDATE user_password_credentials SET password_hash = @h, updated_at = now()", ("h", "new-hash")));
        Assert.Equal("42501", await session.SqlStateOfAsync(
            "UPDATE user_password_credentials SET password_hash = @h, updated_at = now() WHERE user_id = @u",
            ("h", "new-hash"), ("u", await Seed.UserAsync("omar"))));
    }

    // ---- Test 15: an invitation in Al-Amin with a role from Maan → rejected by the composite FK;
    // likewise a cross-tenant membership_roles link.
    [Fact]
    public async Task Test15_CrossTenantRole_RejectedByCompositeFk()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var sara = await Seed.UserAsync("sara");
        var maanOwner = await Seed.RoleAsync(Seed.Maan, "owner");
        await using var session = await Session.OpenAsync(Target.AppUser, user: sara, tenant: alAmin,
            membership: await Seed.MembershipAsync("sara", Seed.AlAmin), scopeAll: true, canManageScope: true);

        Assert.Equal("23503", await session.SqlStateOfAsync(
            "INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) " +
            "VALUES (@id, @t, 'new@seed.test', @r, 'token', 'pending', @u, now() + interval '7 days', now(), 'assigned')",
            ("id", Guid.CreateVersion7()), ("t", alAmin), ("r", maanOwner), ("u", sara)));
        Assert.Equal("23503", await session.SqlStateOfAsync(
            "INSERT INTO membership_roles (id, tenant_id, membership_id, role_id) VALUES (@id, @t, @m, @r)",
            ("id", Guid.CreateVersion7()), ("t", alAmin), ("m", await Seed.MembershipAsync("khaled", Seed.AlAmin)), ("r", maanOwner)));
    }

    // ---- Test 18 (T2 part): assigning to a membership from another tenant → rejected by the composite FK.
    // (Its second part — no membership_scope row → loud at tenant selection — is T3's.)
    [Fact]
    public async Task Test18_AssignmentToAnotherTenantsMembership_RejectedByCompositeFk()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var sara = await Seed.UserAsync("sara");
        await using var session = await Session.OpenAsync(Target.AppUser, user: sara, tenant: alAmin,
            membership: await Seed.MembershipAsync("sara", Seed.AlAmin), scopeAll: true, canManageScope: true);

        Assert.Equal("23503", await session.SqlStateOfAsync(
            "INSERT INTO scope_assignments (id, tenant_id, membership_id, scope_ref_id, assignment_role, active, granted_by, granted_at) " +
            "VALUES (@id, @t, @m, @ref, 'contributor', true, @u, now())",
            ("id", Guid.CreateVersion7()), ("t", alAmin), ("m", await Seed.MembershipAsync("nour", Seed.Maan)),
            ("ref", Guid.CreateVersion7()), ("u", sara)));
    }

    // ---- Test 21: cross-linking via UPDATE, not just INSERT — rejected by the composite constraint.
    // Run as migrator: the only role whose grants even allow these UPDATEs, so the constraint layer is
    // what is tested. (scope_ref_id has no FK in the core — the declared gap, §3.3, 9 — so its part is
    // T5's, where the module's composite FK exists.)
    [Fact]
    public async Task Test21_CrossTenantUpdate_RejectedByCompositeFk()
    {
        var maan = await Seed.TenantAsync(Seed.Maan);
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        var nourInMaan = await Seed.MembershipAsync("nour", Seed.Maan);
        await using var session = await Session.OpenAsync(Target.Migrator);

        Assert.Equal("23503", await session.SqlStateOfAsync(
            "UPDATE membership_roles SET role_id = @r WHERE membership_id = @m", ("r", await Seed.RoleAsync(Seed.Maan, "owner")), ("m", khaled)));
        Assert.Equal("23503", await session.SqlStateOfAsync(
            "UPDATE membership_roles SET tenant_id = @t WHERE membership_id = @m", ("t", maan), ("m", khaled)));
        Assert.Equal("23503", await session.SqlStateOfAsync(
            "UPDATE scope_assignments SET membership_id = @n WHERE membership_id = @m", ("n", nourInMaan), ("m", khaled)));
        Assert.Equal("23503", await session.SqlStateOfAsync(
            "UPDATE memberships SET tenant_id = @t WHERE id = @m", ("t", maan), ("m", khaled)));
    }

    // ---- Test 21, the layer above: app_user has no grant to change these columns at all.
    [Fact]
    public async Task Test21_CrossTenantUpdate_AsAppUser_FailsOnTheGrant()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        await using var session = await Session.OpenAsync(Target.AppUser, user: await Seed.UserAsync("sara"), tenant: alAmin,
            membership: await Seed.MembershipAsync("sara", Seed.AlAmin), scopeAll: true, canManageScope: true);

        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE membership_roles SET role_id = role_id"));
        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE scope_assignments SET membership_id = membership_id"));
        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE memberships SET tenant_id = tenant_id"));
    }

    // ---- Test 23 (T2.4, direct SQL as app_user): the system role is protected, silently below; and the
    // base roles stay readable.
    [Fact]
    public async Task Test23_SystemRoles_ProtectedSilently_ReadableInFull()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        var owner = await Seed.RoleAsync(Seed.AlAmin, "owner");
        await using var session = await Session.OpenAsync(Target.AppUser, user: await Seed.UserAsync("sara"), tenant: alAmin);

        // a. Editing or deleting a system role → zero rows, the row untouched.
        Assert.Equal(0, await session.ExecuteAsync("UPDATE roles SET name_en = 'Hacked' WHERE id = @r", ("r", owner)));
        Assert.Equal(0, await session.ExecuteAsync("DELETE FROM roles WHERE code = 'viewer'"));
        Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM roles WHERE id = @r AND name_en = 'Owner'", ("r", owner)));
        Assert.Equal(1, await session.CountAsync("SELECT count(*) FROM roles WHERE code = 'viewer'"));
        // a. Promoting an ordinary role, or inserting a system role → an error (WITH CHECK).
        Assert.Equal("42501", await session.SqlStateOfAsync("UPDATE roles SET is_system = true WHERE code = 'scope-manager'"));
        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO roles (id, tenant_id, code, name_ar, name_en, is_system, is_active) VALUES (@id, @t, 'fake', 'x', 'x', true, true)",
            ("id", Guid.CreateVersion7()), ("t", alAmin)));

        // c. The opposite direction: the base roles and the custom role are all readable.
        var codes = await session.ListAsync<string>("SELECT code FROM roles");
        foreach (var code in new[] { "owner", "admin", "operator", "viewer", "scope-manager" })
            Assert.Contains(code, codes);
    }
}

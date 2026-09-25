using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Conformance;

// PROOF_SPEC v1.3 T4 — the [B] criteria of bootstrap, invitation, acceptance, departure, member management and
// return (PLATFORM_CORE v1.16 §3.9, §3.10, §4.4, §4.5). What the API commits happens in tenants of the tests' own
// (World: bootstrap through POST /provision/tenants); direct-SQL sessions on the seed always roll back.
public class T4_LifecycleTests
{
    // ---- T4.1 — Test 10: app_user with an admin role → a direct INSERT into memberships → fails.

    [Fact]
    public async Task T4_1_Test10_AppUserWithAdminRole_CannotInsertAMembership()
    {
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        await using var session = await Session.OpenAsync(Target.AppUser, user: await Seed.UserAsync("sara"), tenant: alAmin,
            membership: await Seed.MembershipAsync("sara", Seed.AlAmin), scopeAll: true, canManageScope: true, canManageMembers: true);

        Assert.Equal("42501", await session.SqlStateOfAsync(
            "INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES (@id, @t, @u, 'active', now())",
            ("id", Guid.CreateVersion7()), ("t", alAmin), ("u", await Seed.UserAsync("nour"))));
    }

    // ---- T4.2 — Test 11: an invitation to an existing email and to a non-existent one → identical responses.

    [Fact]
    public async Task T4_2_Test11_InvitationToExistingAndMissingEmail_IdenticalResponses()
    {
        using var world = await World.BootstrapAsync("t4-2");
        var existing = (string)(await World.ScalarObjectAsync(
            "SELECT p.email FROM persons p JOIN users u ON u.person_id = p.id WHERE u.username = 'khaled'"))!;
        var missing = World.Account("t4-2-nobody").Email;

        var (toExisting, _, _) = await world.InviteAsync(world.Owner, existing, "viewer", "assigned");
        var (toMissing, _, _) = await world.InviteAsync(world.Owner, missing, "viewer", "assigned");

        Assert.Equal(HttpStatusCode.Created, toExisting.StatusCode);
        Assert.Equal(toExisting.StatusCode, toMissing.StatusCode);
        var a = await world.Owner.Browser.JsonAsync(toExisting);
        var b = await world.Owner.Browser.JsonAsync(toMissing);
        Assert.Equal(a.EnumerateObject().Select(p => (p.Name, p.Value.ValueKind)), b.EnumerateObject().Select(p => (p.Name, p.Value.ValueKind)));
        Assert.Equal(a.GetProperty("token").GetString()!.Length, b.GetProperty("token").GetString()!.Length);
    }

    // ---- T4.3 — accepting with an authenticated email ≠ the invitation's → rejected, even with a valid token.

    [Fact]
    public async Task T4_3_AcceptWithAnotherAuthenticatedEmail_Rejected_EvenWithAValidToken()
    {
        using var world = await World.BootstrapAsync("t4-3");
        using var other = await World.BootstrapAsync("t4-3-other");
        var (_, invitation, token) = await world.InviteAsync(world.Owner, World.Account("t4-3-invitee").Email, "viewer", "assigned");

        var response = await World.AcceptAsync(other.Owner.Browser, world.TenantId, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("email_mismatch", (await other.Owner.Browser.JsonAsync(response)).GetProperty("error").GetString());
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", world.TenantId), ("u", other.Owner.UserId)));
        Assert.Equal("pending", await InvitationStatusAsync(invitation));

        // And a new account with another email, the same valid token: rejected too, and no account created.
        var (username, _, password) = World.Account("t4-3-stranger");
        using var anonymous = new Browser();
        var stranger = await World.AcceptNewAsync(anonymous, world.TenantId, token, username + "@elsewhere.test", username, password);
        Assert.Equal(HttpStatusCode.Forbidden, stranger.StatusCode);
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));
        Assert.Equal("pending", await InvitationStatusAsync(invitation));
    }

    // ---- T4.4 — a successful acceptance → membership + membership_auth + membership_scope + the invitation updated
    // + an audit entry; and atomically: the transaction killed midway → none of it exists.

    [Fact]
    public async Task T4_4_Acceptance_WritesEveryPart()
    {
        using var world = await World.BootstrapAsync("t4-4");
        var (username, email, password) = World.Account("t4-4-invitee");
        var (_, invitation, token) = await world.InviteAsync(world.Owner, email, "operator", "assigned");
        using var anonymous = new Browser();

        Assert.Equal(HttpStatusCode.NoContent, (await World.AcceptNewAsync(anonymous, world.TenantId, token, email, username, password)).StatusCode);

        var user = await World.ScalarAsync("SELECT id FROM users WHERE username = @u", ("u", username));
        var membership = await World.ScalarAsync("SELECT id FROM memberships WHERE tenant_id = @t AND user_id = @u AND status = 'active'",
            ("t", world.TenantId), ("u", user));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_auth WHERE membership_id = @m AND provider = 'password'", ("m", membership)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'assigned'", ("m", membership)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM membership_roles mr JOIN roles r ON r.id = mr.role_id WHERE mr.membership_id = @m AND r.code = 'operator'", ("m", membership)));
        Assert.Equal("accepted", await InvitationStatusAsync(invitation));
        Assert.True(await Seed.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_id = @m", ("t", world.TenantId), ("m", membership)) >= 1);
        Assert.True(await Seed.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_id = @i", ("t", world.TenantId), ("i", invitation)) >= 2);

        // The invitee signs in and enters the tenant.
        using var browser = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.LoginAsync(username, password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.SelectAsync(world.TenantId)).StatusCode);
    }

    // Killed midway: a migrator transaction holds the invitation row, so the acceptance — having written the identity,
    // the membership, its parts and their audit entries — waits at its last step, the single-use update. The invitation
    // is then revoked and the lock released: the waiting update finds no 'pending' row, and the whole acceptance
    // rolls back. None of what it wrote exists.
    [Fact]
    public async Task T4_4_Acceptance_KilledMidway_NothingExists()
    {
        using var world = await World.BootstrapAsync("t4-4-kill");
        var (username, email, password) = World.Account("t4-4-killed");
        var (_, invitation, token) = await world.InviteAsync(world.Owner, email, "viewer", "assigned");
        var auditBefore = await Seed.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE tenant_id = @t", ("t", world.TenantId));
        var membershipsBefore = await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t", ("t", world.TenantId));

        await using var holder = await Target.OpenAsync(Target.Migrator);
        await using var hold = await holder.BeginTransactionAsync();
        await using (var lockRow = new NpgsqlCommand("SELECT id FROM invitations WHERE id = @i FOR UPDATE", holder, hold))
        {
            lockRow.Parameters.AddWithValue("i", invitation);
            await lockRow.ExecuteNonQueryAsync();
        }

        using var anonymous = new Browser();
        var accepting = World.AcceptNewAsync(anonymous, world.TenantId, token, email, username, password);
        await WaitForAWaiterAsync(accepting, holder.ProcessID);
        // Midway: the acceptance's own writes exist only inside its transaction.
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));

        await using (var revoke = new NpgsqlCommand("UPDATE invitations SET status = 'revoked' WHERE id = @i", holder, hold))
        {
            revoke.Parameters.AddWithValue("i", invitation);
            await revoke.ExecuteNonQueryAsync();
        }
        await hold.CommitAsync();

        var response = await accepting;
        Assert.NotEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM persons WHERE email = @e", ("e", email)));
        Assert.Equal(membershipsBefore, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t", ("t", world.TenantId)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_scope WHERE tenant_id = @t", ("t", world.TenantId)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_auth WHERE tenant_id = @t", ("t", world.TenantId)));
        Assert.Equal(auditBefore, await Seed.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE tenant_id = @t", ("t", world.TenantId)));
        Assert.Equal("revoked", await InvitationStatusAsync(invitation));
    }

    // ---- T4.7 — an invitation with intended_scope_mode = 'all' from a member with no can_manage_scope → fails:
    // at the API, and in the database alone (invitations_insert, item f).

    [Fact]
    public async Task T4_7_AllInvitation_WithoutScopePermission_Fails()
    {
        using var world = await World.BootstrapAsync("t4-7");
        var membersOnly = await world.CustomRoleAsync("core.members.manage");
        using var manager = await world.JoinAsync("t4-7-manager", membersOnly, "assigned");

        var (all, _, _) = await world.InviteAsync(manager, World.Account("t4-7-a").Email, "viewer", "all");
        var (assigned, _, _) = await world.InviteAsync(manager, World.Account("t4-7-b").Email, "viewer", "assigned");

        Assert.Equal(HttpStatusCode.Forbidden, all.StatusCode);
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);

        await using var session = await Session.OpenAsync(Target.AppUser, user: manager.UserId, tenant: world.TenantId,
            membership: manager.MembershipId, scopeAll: false, canManageScope: false, canManageMembers: true);
        Assert.Equal("42501", await session.SqlStateOfAsync(InsertInvitation("all"), InvitationParameters(world, manager)));
        Assert.Equal("no error", await session.SqlStateOfAsync(InsertInvitation("assigned"), InvitationParameters(world, manager)));
    }

    // ---- T4.8 — bootstrap → a tenant + its roles and role permissions from the templates + a first owner member with
    // scope 'all' + an audit entry, in a single transaction (a failure after the tenant was written leaves nothing).

    [Fact]
    public async Task T4_8_Bootstrap_TenantRolesOwnerAudit_InOneTransaction()
    {
        using var world = await World.BootstrapAsync("t4-8");
        var t = ("t", (object)world.TenantId);

        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE id = @t AND status = 'active'", t));
        Assert.Equal(await Seed.CountAsMigratorAsync("SELECT count(*) FROM role_templates"),
            await Seed.CountAsMigratorAsync("SELECT count(*) FROM roles r JOIN role_templates rt ON rt.code = r.code WHERE r.tenant_id = @t AND r.is_system", t));
        Assert.Equal(await Seed.CountAsMigratorAsync("SELECT count(*) FROM role_template_permissions"),
            await Seed.CountAsMigratorAsync(
                """
                SELECT count(*) FROM role_permissions rp JOIN roles r ON r.id = rp.role_id
                JOIN role_templates rt ON rt.code = r.code
                JOIN role_template_permissions rtp ON rtp.template_id = rt.id AND rtp.permission_id = rp.permission_id
                WHERE rp.tenant_id = @t
                """, t));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            """
            SELECT count(*) FROM memberships m
            JOIN membership_scope s ON s.membership_id = m.id AND s.scope_mode = 'all'
            JOIN membership_auth a ON a.membership_id = m.id AND a.provider = 'password'
            JOIN membership_roles mr ON mr.membership_id = m.id JOIN roles r ON r.id = mr.role_id AND r.code = 'owner' AND r.is_system
            WHERE m.tenant_id = @t AND m.status = 'active'
            """, t));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_type = 'tenants' AND entity_id = @t", t));
        Assert.Equal(1, await world.ActiveOwnersAsync());

        // One transaction: a bootstrap that fails after its tenant and roles were written (the username is taken)
        // leaves no tenant, no role, no person behind.
        var name = "T4 t4-8 failed " + World.Tag();
        using var anonymous = new Browser();
        var failed = await anonymous.Client.PostAsJsonAsync("/provision/tenants",
            new { tenant_name = name, full_name = "x", email = World.Account("t4-8-failed").Email, username = world.Owner.Username, password = "x-password" });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM tenants WHERE name = @n", ("n", name)));
    }

    // ---- T4.11 — the acceptance request carries the tenant id beside the token; a tenant id that is not the token's →
    // no invitation (zero rows) → a loud rejection, nothing written.

    [Fact]
    public async Task T4_11_AcceptanceForAnotherTenantId_LoudRejection()
    {
        using var world = await World.BootstrapAsync("t4-11");
        using var other = await World.BootstrapAsync("t4-11-other");
        var (username, email, password) = World.Account("t4-11-invitee");
        var (_, invitation, token) = await world.InviteAsync(world.Owner, email, "viewer", "assigned");
        using var anonymous = new Browser();

        var response = await World.AcceptNewAsync(anonymous, other.TenantId, token, email, username, password);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("invalid_invitation", (await anonymous.JsonAsync(response)).GetProperty("error").GetString());
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));
        Assert.Equal("pending", await InvitationStatusAsync(invitation));

        // Beneath it, as provisioner with the other tenant's id: the invitation is not there.
        await using var session = await Session.OpenAsync(Target.Provisioner, tenant: other.TenantId);
        Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM invitations WHERE id = @i", ("i", invitation)));
    }

    // ---- T4.12 (categorical, §6) — member management in the database, via direct SQL, not the API (§3.10).

    [Fact]
    public async Task T4_12_MemberManagement_InTheDatabase()
    {
        using var world = await World.BootstrapAsync("t4-12");
        using var plain = await world.JoinAsync("t4-12-plain", "viewer", "assigned");
        using var disabled = await world.JoinAsync("t4-12-disabled", "admin", "all");
        using var leaver = await world.JoinAsync("t4-12-left", "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync($"/members/{disabled.MembershipId}/status", new { status = "disabled" })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await leaver.Browser.Client.PostAsync("/me/leave", null)).StatusCode);
        var (_, pending, _) = await world.InviteAsync(world.Owner, World.Account("t4-12-p").Email, "viewer", "assigned");
        var (_, revoked, _) = await world.InviteAsync(world.Owner, World.Account("t4-12-r").Email, "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.Client.PostAsync($"/members/invitations/{revoked}/revoke", null)).StatusCode);
        var viewer = await world.RoleAsync("viewer");
        var owner = world.Owner;

        // A member without core.members.manage cannot disable another, grant or delete a role, invite, or revoke.
        await using (var s = await Session.OpenAsync(Target.AppUser, user: plain.UserId, tenant: world.TenantId, membership: plain.MembershipId,
                         scopeAll: false, canManageScope: false, canManageMembers: false))
        {
            Assert.Equal(0, await s.ExecuteAsync("UPDATE memberships SET status = 'disabled' WHERE id = @m", ("m", owner.MembershipId)));
            Assert.Equal("42501", await s.SqlStateOfAsync(InsertRole(), RoleParameters(world, owner.MembershipId, viewer)));
            Assert.Equal(0, await s.ExecuteAsync("DELETE FROM membership_roles WHERE membership_id = @m", ("m", owner.MembershipId)));
            Assert.Equal("42501", await s.SqlStateOfAsync(InsertInvitation("assigned"), InvitationParameters(world, plain)));
            Assert.Equal(0, await s.ExecuteAsync("UPDATE invitations SET status = 'revoked' WHERE id = @i", ("i", pending)));
        }

        await using (var s = await ManagerSessionAsync(world, owner))
        {
            // No one edits their own roles.
            Assert.Equal("42501", await s.SqlStateOfAsync(InsertRole(), RoleParameters(world, owner.MembershipId, viewer)));
            Assert.Equal(0, await s.ExecuteAsync("DELETE FROM membership_roles WHERE membership_id = @m", ("m", owner.MembershipId)));
            // A manager cannot bring back someone who left.
            Assert.Equal(0, await s.ExecuteAsync("UPDATE memberships SET status = 'active' WHERE id = @m", ("m", leaver.MembershipId)));
            Assert.Equal(0, await s.ExecuteAsync("UPDATE memberships SET status = 'disabled' WHERE id = @m", ("m", leaver.MembershipId)));
            // Invitations: created 'pending' only; revoked pending → revoked only.
            Assert.Equal("42501", await s.SqlStateOfAsync(InsertInvitation("assigned", "accepted"), InvitationParameters(world, owner)));
            Assert.Equal("42501", await s.SqlStateOfAsync("UPDATE invitations SET status = 'accepted' WHERE id = @i", ("i", pending)));
            Assert.Equal("42501", await s.SqlStateOfAsync("UPDATE invitations SET status = 'expired' WHERE id = @i", ("i", pending)));
            Assert.Equal(0, await s.ExecuteAsync("UPDATE invitations SET status = 'pending' WHERE id = @i", ("i", revoked)));
            // And what a manager may do, the same session does: the refusals above are the policies, not a broken session.
            Assert.Equal(1, await s.ExecuteAsync("UPDATE invitations SET status = 'revoked' WHERE id = @i", ("i", pending)));
            Assert.Equal(1, await s.ExecuteAsync("UPDATE memberships SET status = 'disabled' WHERE id = @m", ("m", plain.MembershipId)));
        }

        // A disabled member can neither re-enable themselves nor leave — even holding the members permission.
        await using (var s = await Session.OpenAsync(Target.AppUser, user: disabled.UserId, tenant: world.TenantId,
                         membership: disabled.MembershipId, scopeAll: true, canManageScope: true, canManageMembers: true))
        {
            Assert.Equal(0, await s.ExecuteAsync("UPDATE memberships SET status = 'active' WHERE id = @m", ("m", disabled.MembershipId)));
            Assert.Equal(0, await s.ExecuteAsync("UPDATE memberships SET status = 'left' WHERE id = @m", ("m", disabled.MembershipId)));
        }

        Assert.Equal("disabled", await World.StatusOfAsync(disabled.MembershipId));
        Assert.Equal("left", await World.StatusOfAsync(leaver.MembershipId));
        Assert.Equal("active", await World.StatusOfAsync(plain.MembershipId));
        Assert.Equal("pending", await InvitationStatusAsync(pending));
        Assert.Equal("revoked", await InvitationStatusAsync(revoked));
    }

    // ---- T4.13 [B] — the second layer: the member-management endpoints refuse a member without core.members.manage
    // (403, not_permitted), and nothing changes. The database refusing on its own is T4.12; the absence of any database
    // command before the refusal is the white-box half (Core.WhiteBoxTests, T4_MemberAdministrationTests).

    [Fact]
    public async Task T4_13_Api_RefusesMemberManagement_WithoutThePermission()
    {
        using var world = await World.BootstrapAsync("t4-13");
        using var plain = await world.JoinAsync("t4-13-plain", "operator", "assigned");
        using var target = await world.JoinAsync("t4-13-target", "viewer", "assigned");
        var (_, pending, _) = await world.InviteAsync(world.Owner, World.Account("t4-13-p").Email, "viewer", "assigned");
        var viewer = await world.RoleAsync("viewer");
        var client = plain.Browser.Client;

        HttpResponseMessage[] refused =
        [
            await client.PutAsJsonAsync($"/members/{target.MembershipId}/status", new { status = "disabled" }),
            await client.PostAsJsonAsync($"/members/{target.MembershipId}/roles", new { role_id = await world.RoleAsync("admin") }),
            await client.DeleteAsync($"/members/{target.MembershipId}/roles/{viewer}"),
            await client.PostAsJsonAsync("/members/invitations", new { email = World.Account("t4-13-x").Email, role_id = viewer, intended_scope_mode = "assigned" }),
            await client.PostAsync($"/members/invitations/{pending}/revoke", null),
        ];

        foreach (var response in refused)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("not_permitted", (await plain.Browser.JsonAsync(response)).GetProperty("error").GetString());
        }
        Assert.Equal("active", await World.StatusOfAsync(target.MembershipId));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_roles WHERE membership_id = @m", ("m", target.MembershipId)));
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM invitations WHERE invited_by = @u", ("u", plain.UserId)));
        Assert.Equal("pending", await InvitationStatusAsync(pending));
    }

    // ---- T4.14 — return: a member who left accepts a new invitation → the same row, active, with only the new
    // invitation's role and mode, no old active assignment, the same membership_auth (D9, D10). The wrong order is
    // seen to leave the old ones. A disabled or an active member accepting → refused.

    [Fact]
    public async Task T4_14_Return_SameRow_OnlyTheNewInvitation()
    {
        using var world = await World.BootstrapAsync("t4-14");
        using var member = await world.JoinAsync("t4-14-member", "admin", "assigned");
        var assignment = Guid.CreateVersion7();
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{member.MembershipId}/assignments/{assignment}", new { active = true, reason = "t4.14" })).Status);
        var auth = await World.ScalarAsync("SELECT id FROM membership_auth WHERE membership_id = @m", ("m", member.MembershipId));
        Assert.Equal(HttpStatusCode.NoContent, (await member.Browser.Client.PostAsync("/me/leave", null)).StatusCode);

        var (_, _, token) = await world.InviteAsync(world.Owner, member.Email, "viewer", "all");
        var response = await World.AcceptAsync(member.Browser, world.TenantId, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", world.TenantId), ("u", member.UserId)));
        Assert.Equal("active", await World.StatusOfAsync(member.MembershipId));
        Assert.Equal(["viewer"], await RoleCodesAsync(member.MembershipId));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = 'all'", ("m", member.MembershipId)));
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM scope_assignments WHERE membership_id = @m AND active", ("m", member.MembershipId)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM scope_assignments WHERE membership_id = @m AND NOT active", ("m", member.MembershipId)));
        Assert.Equal(auth, await World.ScalarAsync("SELECT id FROM membership_auth WHERE membership_id = @m", ("m", member.MembershipId)));
        Assert.Equal(HttpStatusCode.NoContent, (await member.Browser.SelectAsync(world.TenantId)).StatusCode);
    }

    [Fact]
    public async Task T4_14_Return_TheWrongOrder_LeavesTheOldRolesAndAssignments()
    {
        using var world = await World.BootstrapAsync("t4-14-wrong");
        using var member = await world.JoinAsync("t4-14-wrong", "admin", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{member.MembershipId}/assignments/{Guid.CreateVersion7()}", new { active = true, reason = "t4.14" })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await member.Browser.Client.PostAsync("/me/leave", null)).StatusCode);

        // The binding order, as provisioner: while the row is 'left', each step reaches its rows.
        await using (var right = await Session.OpenAsync(Target.Provisioner, tenant: world.TenantId))
        {
            Assert.Equal(1, await right.ExecuteAsync("DELETE FROM membership_roles WHERE membership_id = @m", ("m", member.MembershipId)));
            Assert.Equal(1, await right.ExecuteAsync("UPDATE scope_assignments SET active = false WHERE membership_id = @m AND active", ("m", member.MembershipId)));
            Assert.Equal(1, await right.ExecuteAsync("UPDATE memberships SET status = 'active' WHERE id = @m", ("m", member.MembershipId)));
        }

        // The wrong order — re-activating first: the steps after it reach nothing, and the old ones stay.
        await using (var wrong = await Session.OpenAsync(Target.Provisioner, tenant: world.TenantId))
        {
            Assert.Equal(1, await wrong.ExecuteAsync("UPDATE memberships SET status = 'active' WHERE id = @m", ("m", member.MembershipId)));
            Assert.Equal(0, await wrong.ExecuteAsync("DELETE FROM membership_roles WHERE membership_id = @m", ("m", member.MembershipId)));
            Assert.Equal(0, await wrong.ExecuteAsync("UPDATE scope_assignments SET active = false WHERE membership_id = @m AND active", ("m", member.MembershipId)));
            Assert.Equal(0, await wrong.ExecuteAsync("UPDATE membership_scope SET scope_mode = 'all' WHERE membership_id = @m", ("m", member.MembershipId)));
        }
        Assert.Equal(["admin"], await RoleCodesAsync(member.MembershipId));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM scope_assignments WHERE membership_id = @m AND active", ("m", member.MembershipId)));
    }

    [Fact]
    public async Task T4_14_DisabledOrActiveMember_Accepting_Refused()
    {
        using var world = await World.BootstrapAsync("t4-14-refused");
        using var active = await world.JoinAsync("t4-14-active", "viewer", "assigned");
        using var disabled = await world.JoinAsync("t4-14-disabled", "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync($"/members/{disabled.MembershipId}/status", new { status = "disabled" })).Status);

        foreach (var member in new[] { active, disabled })
        {
            var (_, invitation, token) = await world.InviteAsync(world.Owner, member.Email, "admin", "all");
            var response = await World.AcceptAsync(member.Browser, world.TenantId, token);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("already_member", (await member.Browser.JsonAsync(response)).GetProperty("error").GetString());
            Assert.Equal(["viewer"], await RoleCodesAsync(member.MembershipId));
            Assert.Equal("pending", await InvitationStatusAsync(invitation));
        }
        Assert.Equal("active", await World.StatusOfAsync(active.MembershipId));
        Assert.Equal("disabled", await World.StatusOfAsync(disabled.MembershipId));
    }

    // ---- helpers

    private static Task<Session> ManagerSessionAsync(World world, Member manager) =>
        Session.OpenAsync(Target.AppUser, user: manager.UserId, tenant: world.TenantId, membership: manager.MembershipId,
            scopeAll: true, canManageScope: true, canManageMembers: true);

    private static string InsertRole() =>
        "INSERT INTO membership_roles (id, tenant_id, membership_id, role_id) VALUES (@id, @t, @m, @r)";

    private static (string, object)[] RoleParameters(World world, Guid membership, Guid role) =>
        [("id", Guid.CreateVersion7()), ("t", world.TenantId), ("m", membership), ("r", role)];

    private static string InsertInvitation(string mode, string status = "pending") =>
        "INSERT INTO invitations (id, tenant_id, email, role_id, token_hash, status, invited_by, expires_at, created_at, intended_scope_mode) " +
        $"VALUES (@id, @t, @e, (SELECT id FROM roles WHERE tenant_id = @t AND code = 'viewer'), 'x', '{status}', @u, now() + interval '1 day', now(), '{mode}')";

    private static (string, object)[] InvitationParameters(World world, Member by) =>
        [("id", Guid.CreateVersion7()), ("t", world.TenantId), ("e", World.Account("t4-sql").Email), ("u", by.UserId)];

    private static async Task<string> InvitationStatusAsync(Guid invitation) =>
        (string)(await World.ScalarObjectAsync("SELECT status FROM invitations WHERE id = @i", ("i", invitation)))!;

    private static async Task<List<string>> RoleCodesAsync(Guid membership)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(
            "SELECT r.code FROM membership_roles mr JOIN roles r ON r.id = mr.role_id WHERE mr.membership_id = @m ORDER BY r.code", connection);
        command.Parameters.AddWithValue("m", membership);
        return await Target.QueryStringsAsync(command);
    }

    // Waits until a backend is blocked by the holder — the acceptance, at its single-use update — or fails if the
    // request ends first.
    private static async Task WaitForAWaiterAsync(Task<HttpResponseMessage> request, int holder)
    {
        for (var i = 0; i < 200; i++)
        {
            if (request.IsCompleted)
                throw new Xunit.Sdk.XunitException($"The acceptance ended before it waited: {(await request).StatusCode}");
            if (await Seed.CountAsMigratorAsync("SELECT count(*) FROM pg_stat_activity WHERE @h = ANY (pg_blocking_pids(pid))", ("h", holder)) > 0)
                return;
            await Task.Delay(50);
        }
        throw new Xunit.Sdk.XunitException("The acceptance never waited on the held invitation row.");
    }
}

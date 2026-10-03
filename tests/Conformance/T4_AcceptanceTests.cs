using System.Net;
using System.Net.Http.Json;

namespace Conformance;

// Invitation acceptance — option C, decided by the project owner (docs/proposals/v1.17-acceptance) [B]. The six
// conditions of the acceptance, on its three paths — a new account, a return, an existing account — through the API:
//   1. the token: known, unconsumed, unexpired;
//   2. the account's address is the invitation's, surrounding spaces and case aside — and only then;
//   3. the membership: none → created; 'left' → it returns; active → refused;
//   4. one transaction: the membership, its scope in the invitation's mode, the invitation's role, the token consumed;
//   5. two acceptances of one token at once: one succeeds;
//   6. accepting selects no tenant.
// With no session, an address that has an account is not registered again: sign in, then accept. What the API
// commits happens in tenants of the tests' own (World); every check of what was written reads as migrator.
public class T4_AcceptanceTests
{
    // ---- the new-account path

    // The account's address is the invitation's in another case and with spaces around it: one transaction — the
    // person, the membership, its scope in the invitation's mode, the invitation's role, its provider, the token.
    [Fact]
    public async Task NewAccount_TheAddressInAnotherCaseWithSpaces_JoinsWithEveryPart()
    {
        using var world = await World.BootstrapAsync("acc-new");
        var (username, email, password) = World.Account("acc-new-invitee");
        var (invitation, token) = await InviteAsync(world, email, "viewer", "all");

        using var anonymous = new Browser();
        var response = await World.AcceptNewAsync(anonymous, world.TenantId, token, "  " + email.ToUpperInvariant() + " ", username, password);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var user = await World.ScalarAsync("SELECT id FROM users WHERE username = @u", ("u", username));
        await AssertJoinedAsync(world, user, invitation, "viewer", "all");
    }

    // An address with an account — in another case — is not registered again (the closing paragraph): account_exists,
    // nothing written, the token still pending for the signed-in path.
    [Fact]
    public async Task NewAccount_AnAddressWithAnAccount_InAnotherCase_IsRefused_SignInFirst()
    {
        using var world = await World.BootstrapAsync("acc-exists");
        using var home = await World.BootstrapAsync("acc-exists-home");
        var (invitation, token) = await InviteAsync(world, home.Owner.Email, "viewer", "assigned");
        var (username, _, password) = World.Account("acc-exists-second");

        using var anonymous = new Browser();
        var response = await World.AcceptNewAsync(anonymous, world.TenantId, token, " " + home.Owner.Email.ToUpperInvariant(), username, password);

        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "account_exists");
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM persons WHERE lower(btrim(email)) = lower(btrim(@e))", ("e", home.Owner.Email)));
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));
        await AssertNothingWrittenAsync(world, invitation);
    }

    // A username already taken is its own refusal — not "sign in": the address has no account.
    [Fact]
    public async Task NewAccount_AUsernameTaken_IsRefused_AsSuch()
    {
        using var world = await World.BootstrapAsync("acc-username");
        var (_, email, password) = World.Account("acc-username-invitee");
        var (invitation, token) = await InviteAsync(world, email, "viewer", "assigned");

        using var anonymous = new Browser();
        var response = await World.AcceptNewAsync(anonymous, world.TenantId, token, email, world.Owner.Username, password);

        await AssertRefusedAsync(response, HttpStatusCode.Conflict, "username_taken");
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM persons WHERE email = @e", ("e", email)));
        await AssertNothingWrittenAsync(world, invitation);
    }

    // ---- the existing-account path

    // A member of another organization, signed in, accepts an invitation addressed to them in another case and with
    // spaces: a second membership, every part — and the organization they were in stays the one selected (6).
    [Fact]
    public async Task ExistingAccount_ASecondOrganization_TheAddressInAnotherCase_Joins_AndSelectsNothing()
    {
        using var world = await World.BootstrapAsync("acc-existing");
        using var home = await World.BootstrapAsync("acc-existing-home");
        var (invitation, token) = await InviteAsync(world, " " + home.Owner.Email.ToUpperInvariant() + "  ", "viewer", "assigned");

        var response = await World.AcceptAsync(home.Owner.Browser, world.TenantId, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertJoinedAsync(world, home.Owner.UserId, invitation, "viewer", "assigned");
        var (status, me) = await home.Owner.Browser.GetAsync("/me");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(home.TenantId, me.GetProperty("active_tenant").GetProperty("tenant_id").GetGuid());
    }

    // Signed in with no tenant selected: after accepting, still none (6) — the selection enters, never the acceptance.
    [Fact]
    public async Task ExistingAccount_NoTenantSelected_StaysWithNone()
    {
        using var world = await World.BootstrapAsync("acc-none");
        using var home = await World.BootstrapAsync("acc-none-home");
        var (_, token) = await InviteAsync(world, home.Owner.Email, "viewer", "assigned");
        using var browser = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await browser.LoginAsync(home.Owner.Username, home.Owner.Password)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await World.AcceptAsync(browser, world.TenantId, token)).StatusCode);

        var (_, me) = await browser.GetAsync("/me");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, me.GetProperty("active_tenant").ValueKind);
    }

    // An active membership in the tenant already → refused (3): already_member, the token still pending.
    [Fact]
    public async Task ExistingAccount_AnActiveMembership_IsRefused()
    {
        using var world = await World.BootstrapAsync("acc-active");
        using var member = await world.JoinAsync("acc-active-member", "viewer", "assigned");
        var (invitation, token) = await InviteAsync(world, member.Email.ToUpperInvariant(), "viewer", "all");

        await AssertRefusedAsync(await World.AcceptAsync(member.Browser, world.TenantId, token), HttpStatusCode.Forbidden, "already_member");
        Assert.Equal("pending", await StatusOfInvitationAsync(invitation));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", world.TenantId), ("u", member.UserId)));
    }

    // ---- the return path

    // A member who left returns by an invitation addressed to them in another case: the same row, active again, in
    // the new invitation's role and mode, the token consumed.
    [Fact]
    public async Task Return_TheAddressInAnotherCase_TheSameMembershipReturns()
    {
        using var world = await World.BootstrapAsync("acc-return");
        using var leaver = await world.JoinAsync("acc-return-leaver", "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await leaver.Browser.Client.PostAsync("/me/leave", null)).StatusCode);
        var (invitation, token) = await InviteAsync(world, "  " + leaver.Email.ToUpperInvariant(), "admin", "all");

        Assert.Equal(HttpStatusCode.NoContent, (await World.AcceptAsync(leaver.Browser, world.TenantId, token)).StatusCode);

        Assert.Equal("active", await World.StatusOfAsync(leaver.MembershipId));
        await AssertJoinedAsync(world, leaver.UserId, invitation, "admin", "all");
    }

    // ---- condition 2: a different address is refused, however it is written

    public static TheoryData<string> OtherAddresses => new()
    {
        "  {0}-OTHER@T4.TEST ",      // another address, in another case, with spaces around it
        "{0} @t4.test",               // a space inside is not trimmed: another address
        "{0}@t4.test.other",          // the address as a prefix of another
    };

    // Signed in, the invitation addressed to someone else: email_mismatch from the API, no membership, the token
    // still pending.
    [Theory]
    [MemberData(nameof(OtherAddresses))]
    public async Task ExistingAccount_AnotherAddress_IsRefused_NoMembership(string pattern)
    {
        using var world = await World.BootstrapAsync("acc-mismatch");
        using var home = await World.BootstrapAsync("acc-mismatch-home");
        var local = home.Owner.Email[..home.Owner.Email.IndexOf('@')];
        var (invitation, token) = await InviteAsync(world, string.Format(pattern, local), "viewer", "assigned");

        await AssertRefusedAsync(await World.AcceptAsync(home.Owner.Browser, world.TenantId, token), HttpStatusCode.Forbidden, "email_mismatch");
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", world.TenantId), ("u", home.Owner.UserId)));
        await AssertNothingWrittenAsync(world, invitation);
    }

    // With no session, a new account whose address is not the invitation's: refused, nothing created.
    [Theory]
    [MemberData(nameof(OtherAddresses))]
    public async Task NewAccount_AnotherAddress_IsRefused_NothingCreated(string pattern)
    {
        using var world = await World.BootstrapAsync("acc-mismatch-new");
        var (username, email, password) = World.Account("acc-mismatch-new");
        var (invitation, token) = await InviteAsync(world, email, "viewer", "assigned");
        var other = string.Format(pattern, email[..email.IndexOf('@')]);

        using var anonymous = new Browser();
        await AssertRefusedAsync(await World.AcceptNewAsync(anonymous, world.TenantId, token, other, username, password),
            HttpStatusCode.Forbidden, "email_mismatch");
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM users WHERE username = @u", ("u", username)));
        await AssertNothingWrittenAsync(world, invitation);
    }

    // ---- condition 1: the token

    [Fact]
    public async Task Token_UnknownConsumedOrExpired_IsRefused()
    {
        using var world = await World.BootstrapAsync("acc-token");
        using var home = await World.BootstrapAsync("acc-token-home");

        await AssertRefusedAsync(await World.AcceptAsync(home.Owner.Browser, world.TenantId, "not-a-token"), HttpStatusCode.Forbidden, "invalid_invitation");

        var (expired, expiredToken) = await InviteAsync(world, home.Owner.Email, "viewer", "assigned");
        await World.ExecuteAsMigratorAsync("UPDATE invitations SET expires_at = now() - interval '1 minute' WHERE id = @i", ("i", expired));
        await AssertRefusedAsync(await World.AcceptAsync(home.Owner.Browser, world.TenantId, expiredToken), HttpStatusCode.Forbidden, "invitation_expired");

        using var member = await world.JoinAsync("acc-token-member", "viewer", "assigned");
        var consumedToken = await ConsumedTokenAsync(world, member);
        await AssertRefusedAsync(await World.AcceptAsync(home.Owner.Browser, world.TenantId, consumedToken), HttpStatusCode.Forbidden, "invalid_invitation");

        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", world.TenantId), ("u", home.Owner.UserId)));
    }

    // ---- condition 5: one token, accepted at once

    // Two users of one person — both match the address, so only the token stands between them — each signed in,
    // accept the same token many times at once: exactly one acceptance succeeds, one membership exists.
    [Fact]
    public async Task OneToken_AcceptedAtOnce_ByTwoUsersOfOnePerson_OneSucceeds()
    {
        using var world = await World.BootstrapAsync("acc-race");
        using var home = await World.BootstrapAsync("acc-race-home");
        var (invitation, token) = await InviteAsync(world, home.Owner.Email, "viewer", "assigned");
        var second = Guid.CreateVersion7();
        var secondName = World.Account("acc-race-second").Username;
        await World.ExecuteAsMigratorAsync(
            """
            INSERT INTO users (id, person_id, user_type, username, status) SELECT @id, person_id, 'customer', @n, 'active' FROM users WHERE id = @u;
            INSERT INTO user_password_credentials (user_id, password_hash, updated_at) SELECT @id, password_hash, now() FROM user_password_credentials WHERE user_id = @u;
            """, ("id", second), ("n", secondName), ("u", home.Owner.UserId));
        using var other = new Browser();
        Assert.Equal(HttpStatusCode.NoContent, (await other.LoginAsync(secondName, home.Owner.Password)).StatusCode);

        var statuses = await World.ConcurrentlyAsync(Enumerable.Range(0, 8)
            .Select(i => (Func<Task<HttpResponseMessage>>)(() => World.AcceptAsync(i % 2 == 0 ? home.Owner.Browser : other, world.TenantId, token)))
            .ToArray());

        Assert.Single(statuses, s => s == HttpStatusCode.NoContent);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id IN (@a, @b)",
            ("t", world.TenantId), ("a", home.Owner.UserId), ("b", second)));
        Assert.Equal("accepted", await StatusOfInvitationAsync(invitation));
    }

    // ---- CSRF: a token sent from a foreign origin, signed in

    [Fact]
    public async Task ExistingAccount_FromAForeignOrigin_IsRefused_NothingWritten()
    {
        using var world = await World.BootstrapAsync("acc-csrf");
        using var home = await World.BootstrapAsync("acc-csrf-home");
        var (invitation, token) = await InviteAsync(world, home.Owner.Email, "viewer", "assigned");
        using var bare = new Browser(platformHeaders: false);
        foreach (Cookie cookie in home.Owner.Browser.Cookies.GetCookies(Browser.BaseUrl))
            bare.Cookies.Add(cookie);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/invitations/accept")
        {
            Content = JsonContent.Create(new { tenant_id = world.TenantId, token }),
        };
        request.Headers.Add("Origin", "https://attacker.example");
        request.Headers.Add(Browser.RequestedWithHeader, Browser.RequestedWith);

        await AssertRefusedAsync(await bare.Client.SendAsync(request), HttpStatusCode.Forbidden, "csrf_rejected");
        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t AND user_id = @u",
            ("t", world.TenantId), ("u", home.Owner.UserId)));
        await AssertNothingWrittenAsync(world, invitation);
    }

    // ---- helpers

    private static async Task<(Guid Invitation, string Token)> InviteAsync(World world, string email, string role, string mode)
    {
        var (response, invitation, token) = await world.InviteAsync(world.Owner, email, role, mode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (invitation, token);
    }

    /// <summary>A token already used: an invitation to a fresh member, accepted by them.</summary>
    private static async Task<string> ConsumedTokenAsync(World world, Member member)
    {
        // The member's own invitation was consumed when they joined; its token is not stored, so a new one is used up.
        Assert.Equal(HttpStatusCode.NoContent, (await member.Browser.Client.PostAsync("/me/leave", null)).StatusCode);
        var (_, token) = await InviteAsync(world, member.Email, "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await World.AcceptAsync(member.Browser, world.TenantId, token)).StatusCode);
        return token;
    }

    private static async Task<string> StatusOfInvitationAsync(Guid invitation) =>
        (string)(await World.ScalarObjectAsync("SELECT status FROM invitations WHERE id = @i", ("i", invitation)))!;

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("error").GetString());
    }

    /// <summary>A refused acceptance left the tenant as it was: the owner its only member, the token pending.</summary>
    private static async Task AssertNothingWrittenAsync(World world, Guid invitation)
    {
        Assert.Equal("pending", await StatusOfInvitationAsync(invitation));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM memberships WHERE tenant_id = @t", ("t", world.TenantId)));
    }

    /// <summary>Condition 4: the membership active, its one scope row in the invitation's mode, the invitation's role, its provider, the token consumed.</summary>
    private static async Task AssertJoinedAsync(World world, Guid user, Guid invitation, string role, string mode)
    {
        var membership = await World.ScalarAsync("SELECT id FROM memberships WHERE tenant_id = @t AND user_id = @u AND status = 'active'",
            ("t", world.TenantId), ("u", user));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_scope WHERE membership_id = @m AND scope_mode = @s",
            ("m", membership), ("s", mode)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_scope WHERE membership_id = @m", ("m", membership)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM membership_roles mr JOIN invitations i ON i.role_id = mr.role_id JOIN roles r ON r.id = mr.role_id WHERE mr.membership_id = @m AND i.id = @i AND r.code = @r",
            ("m", membership), ("i", invitation), ("r", role)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_roles WHERE membership_id = @m", ("m", membership)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM membership_auth WHERE membership_id = @m", ("m", membership)));
        Assert.Equal("accepted", await StatusOfInvitationAsync(invitation));
    }
}

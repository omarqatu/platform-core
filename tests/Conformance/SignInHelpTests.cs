using System.Net;
using System.Net.Http.Json;

namespace Conformance;

/// <summary>
/// Sign-in help (the project owner's item 37, group ج): <c>POST /api/members/{membershipId}/sign-in-help</c>. A member
/// manager sends a member a plain message pointing them at the forgot-password page — no token, no new permission, no
/// write on <c>password_reset_tokens</c> — and the act is recorded in <c>audit_log</c> as
/// <c>sign_in_help_sent</c>. Two limits, both marked assumptions <c>[—]</c> like OPEN_ITEMS 106: one per member per
/// <c>[15 minutes]</c>, <c>[20]</c> per organization per <c>[24 hours]</c>, both <c>429 too_many_attempts</c>.
/// <para>
/// Refusals use the codes the same states already answer elsewhere: a membership that is not active, not this
/// organization's, or the caller's own is <c>403 not_permitted</c>, as <c>PUT /members/{id}/status</c> answers a
/// membership its write policy does not admit; an undeliverable address is <c>400 invalid_value</c>, as acceptance
/// answers it since item 35. A refusal writes nothing and consumes no allowance.
/// </para>
/// </summary>
public class SignInHelpTests
{
    private const int PerTenant = 20;

    [Fact]
    public async Task AnActiveMember_IsSentHelp_AndTheActIsAuditedOnce()
    {
        using var world = await World.BootstrapAsync("b37-sent");
        using var member = await world.JoinAsync("b37-sent-member", "viewer", "assigned");

        var response = await HelpAsync(world.Owner, member.MembershipId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(1, await AuditedAsync(member.MembershipId));
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            """
            SELECT count(*) FROM audit_log
             WHERE tenant_id = @t AND entity_id = @m AND action = 'sign_in_help_sent' AND entity_type = 'memberships'
               AND actor_id = @a AND actor_type = 'user' AND old_value IS NULL AND new_value = '{"channel": "email"}'::jsonb
            """, ("t", world.TenantId), ("m", member.MembershipId), ("a", world.Owner.UserId)));
    }

    // ---- Refused, nothing audited.

    [Fact]
    public async Task WithoutMemberManagement_IsNotPermitted()
    {
        using var world = await World.BootstrapAsync("b37-perm");
        using var member = await world.JoinAsync("b37-perm-member", "viewer", "assigned");
        using var other = await world.JoinAsync("b37-perm-other", "viewer", "assigned");

        await AssertRefusedAsync(await HelpAsync(member, other.MembershipId), HttpStatusCode.Forbidden, "not_permitted");
        Assert.Equal(0, await AuditedAsync(other.MembershipId));
    }

    [Fact]
    public async Task TheCallersOwnMembership_IsNotPermitted()
    {
        using var world = await World.BootstrapAsync("b37-self");

        await AssertRefusedAsync(await HelpAsync(world.Owner, world.Owner.MembershipId), HttpStatusCode.Forbidden, "not_permitted");
        Assert.Equal(0, await AuditedAsync(world.Owner.MembershipId));
    }

    [Fact]
    public async Task AnotherOrganizationsMembership_IsNotPermitted()
    {
        using var world = await World.BootstrapAsync("b37-other");
        using var elsewhere = await World.BootstrapAsync("b37-other-elsewhere");
        using var stranger = await elsewhere.JoinAsync("b37-other-stranger", "viewer", "assigned");

        await AssertRefusedAsync(await HelpAsync(world.Owner, stranger.MembershipId), HttpStatusCode.Forbidden, "not_permitted");
        Assert.Equal(0, await AuditedAsync(stranger.MembershipId));
    }

    // Disabled cannot sign in to this organization at all, and left is no longer a member: the message would send
    // them to a page that cannot help. The same code PUT /members/{id}/status answers for a 'left' membership.
    [Fact]
    public async Task ADisabledOrLeftMember_IsNotPermitted()
    {
        using var world = await World.BootstrapAsync("b37-inactive");
        using var disabled = await world.JoinAsync("b37-inactive-disabled", "viewer", "assigned");
        using var leaver = await world.JoinAsync("b37-inactive-left", "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.Client.PutAsJsonAsync(
            $"/members/{disabled.MembershipId}/status", new { status = "disabled" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await leaver.Browser.Client.PostAsync("/me/leave", null)).StatusCode);

        foreach (var member in new[] { disabled, leaver })
        {
            await AssertRefusedAsync(await HelpAsync(world.Owner, member.MembershipId), HttpStatusCode.Forbidden, "not_permitted");
            Assert.Equal(0, await AuditedAsync(member.MembershipId));
        }
    }

    // An address written before item 35 that no message can reach — planted as migrator, as acceptance's own test
    // plants one. The same state answers the same code: 400 invalid_value. Nothing audited, no allowance consumed.
    [Fact]
    public async Task AnUndeliverableAddress_IsInvalidValue_AndConsumesNothing()
    {
        using var world = await World.BootstrapAsync("b37-address");
        using var member = await world.JoinAsync("b37-address-member", "viewer", "assigned");
        var address = await AddressOfAsync(member);
        await SetAddressAsync(member, "hasan-" + World.Tag());

        await AssertRefusedAsync(await HelpAsync(world.Owner, member.MembershipId), HttpStatusCode.BadRequest, "invalid_value");
        Assert.Equal(0, await AuditedAsync(member.MembershipId));

        await SetAddressAsync(member, address);
        Assert.Equal(HttpStatusCode.NoContent, (await HelpAsync(world.Owner, member.MembershipId)).StatusCode);
    }

    // ---- The two limits.

    [Fact]
    public async Task OneMember_IsSentHelpOnceInTheWindow()
    {
        using var world = await World.BootstrapAsync("b37-per-member");
        using var member = await world.JoinAsync("b37-per-member-m", "viewer", "assigned");
        using var other = await world.JoinAsync("b37-per-member-o", "viewer", "assigned");

        Assert.Equal(HttpStatusCode.NoContent, (await HelpAsync(world.Owner, member.MembershipId)).StatusCode);
        await AssertRefusedAsync(await HelpAsync(world.Owner, member.MembershipId), HttpStatusCode.TooManyRequests, "too_many_attempts");
        Assert.Equal(1, await AuditedAsync(member.MembershipId));

        // Per member: another member of the same organization is still sent help.
        Assert.Equal(HttpStatusCode.NoContent, (await HelpAsync(world.Owner, other.MembershipId)).StatusCode);
    }

    // More members than one inviter may invite in an hour (OPEN_ITEMS 106), so three inviters share them: the
    // owner, and two members holding core.members.manage. The ceiling is the organization's, whoever sends.
    [Fact]
    public async Task OneOrganization_IsStoppedAtItsCeiling_AndAnotherIsNot()
    {
        using var busy = await World.BootstrapAsync("b37-per-tenant");
        using var quiet = await World.BootstrapAsync("b37-per-tenant-quiet");
        var manager = await busy.CustomRoleAsync("core.members.manage");
        var first = await busy.JoinAsync("b37-pt-manager-1", manager, "assigned");
        var second = await busy.JoinAsync("b37-pt-manager-2", manager, "assigned");
        var members = new List<Member> { first, second };
        for (var i = 0; members.Count < PerTenant + 1; i++)
            members.Add(await busy.JoinAsync($"b37-pt-{i}", "viewer", "assigned", by: (i / 8) switch { 0 => null, 1 => first, _ => second }));
        var quietMember = await quiet.JoinAsync("b37-pt-quiet", "viewer", "assigned");

        var statuses = new List<HttpStatusCode>();
        foreach (var member in members)
            statuses.Add((await HelpAsync(busy.Owner, member.MembershipId)).StatusCode);

        Assert.Equal(PerTenant, statuses.Count(s => s == HttpStatusCode.NoContent));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(0, await AuditedAsync(members[^1].MembershipId));
        Assert.Equal(HttpStatusCode.NoContent, (await HelpAsync(quiet.Owner, quietMember.MembershipId)).StatusCode);
    }

    private static Task<HttpResponseMessage> HelpAsync(Member by, Guid membershipId) =>
        by.Browser.Client.PostAsync($"/members/{membershipId}/sign-in-help", null);

    private static async Task AssertRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetString());
    }

    private static Task<long> AuditedAsync(Guid membershipId) => Seed.CountAsMigratorAsync(
        "SELECT count(*) FROM audit_log WHERE entity_id = @m AND action = 'sign_in_help_sent'", ("m", membershipId));

    private static async Task<string> AddressOfAsync(Member member) =>
        (string)(await World.ScalarObjectAsync(
            "SELECT p.email FROM persons p JOIN users u ON u.person_id = p.id WHERE u.id = @u", ("u", member.UserId)))!;

    private static Task SetAddressAsync(Member member, string address) => World.ExecuteAsMigratorAsync(
        "UPDATE persons SET email = @e WHERE id = (SELECT person_id FROM users WHERE id = @u)", ("e", address), ("u", member.UserId));
}

using System.Net;
using System.Net.Http.Json;

namespace Conformance;

/// <summary>
/// The invitation's address (the project owner's item 35, group ج): the server refuses what cannot be delivered,
/// and what would collide at acceptance, before a row is written or a message is queued.
/// <para>
/// <b>What led to it.</b> Two invitations, to <c>adnan</c> and to <c>hasan</c>, were accepted in production. Probing
/// the sender then showed the larger hole: .NET's <c>MailMessage</c> reads <c>a@x.com, b@y.com</c> as two recipients
/// and sends the acceptance link to both. The invitation limit of OPEN_ITEMS 106 counts invitations, not recipients,
/// so a comma multiplies it — and the second address is one nobody chose to invite.
/// </para>
/// </summary>
public class InvitationAddressTests
{
    // ---- What cannot be delivered: 400 invalid_value, and no row.

    public static TheoryData<string> Undeliverable => new()
    {
        "adnan-{0}",                     // no @ at all — the production rows
        "adnan-{0}@",                    // no domain
        "@{0}.test",                     // no local part
        "adnan-{0}@localhost",           // a domain with no dot
        "adnan {0}@b35.test",            // a space inside: MailMessage sends to the part after it
        "\"Adnan\" <adnan-{0}@b35.test>", // a display name: the row's address is not the one sent to
    };

    [Theory]
    [MemberData(nameof(Undeliverable))]
    public async Task AnUndeliverableAddress_IsRefused_AndNoRowIsWritten(string pattern)
    {
        using var world = await World.BootstrapAsync("b35-shape");
        var email = string.Format(pattern, World.Tag());

        var (response, _, _) = await world.InviteAsync(world.Owner, email, "viewer", "assigned");

        await AssertRefusedAsync(world, response, HttpStatusCode.BadRequest, "invalid_value");
        Assert.Equal(0, await InvitationsToAsync(world, email));
    }

    // ---- More than one recipient: the finding that leads this group.

    public static TheoryData<string> SeveralRecipients => new()
    {
        "a-{0}@b35.test, b-{0}@b35.test", // MailMessage: two recipients, both sent the link
        "a-{0}@b35.test,b-{0}@b35.test",
        "a-{0}@b35.test;b-{0}@b35.test",
    };

    [Theory]
    [MemberData(nameof(SeveralRecipients))]
    public async Task AnAddressNamingSeveralRecipients_IsRefused_AndNoRowIsWritten(string pattern)
    {
        using var world = await World.BootstrapAsync("b35-many");
        var email = string.Format(pattern, World.Tag());

        var (response, _, _) = await world.InviteAsync(world.Owner, email, "viewer", "assigned");

        await AssertRefusedAsync(world, response, HttpStatusCode.BadRequest, "invalid_value");
        Assert.Equal(0, await InvitationsToAsync(world, email));
    }

    // ---- An address that already belongs to a member of THIS organization.

    // Active: the invitee would be refused already_member at acceptance, after the message went out. Refused at
    // creation instead — under the comparison rule of EmailAddress (case and surrounding spaces aside), the one the
    // acceptance applies.
    [Fact]
    public async Task AnActiveMembersAddress_IsRefusedAlreadyMember_HoweverItIsWritten()
    {
        using var world = await World.BootstrapAsync("b35-active");
        using var member = await world.JoinAsync("b35-active-member", "viewer", "assigned");

        foreach (var email in new[] { member.Email, "  " + member.Email.ToUpperInvariant() + " " })
        {
            var (response, _, _) = await world.InviteAsync(world.Owner, email, "viewer", "assigned");
            await AssertRefusedAsync(world, response, HttpStatusCode.Conflict, "already_member");
        }
        Assert.Equal(1, await InvitationsToAsync(world, member.Email)); // the one JoinAsync accepted, nothing since
    }

    // Disabled: acceptance refuses it too. The way back for a disabled member is to be enabled, not invited.
    [Fact]
    public async Task ADisabledMembersAddress_IsRefusedAlreadyMember()
    {
        using var world = await World.BootstrapAsync("b35-disabled");
        using var member = await world.JoinAsync("b35-disabled-member", "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.Client.PutAsJsonAsync(
            $"/members/{member.MembershipId}/status", new { status = "disabled" })).StatusCode);

        var (response, _, _) = await world.InviteAsync(world.Owner, member.Email, "viewer", "assigned");

        await AssertRefusedAsync(world, response, HttpStatusCode.Conflict, "already_member");
        Assert.Equal(1, await InvitationsToAsync(world, member.Email));
    }

    // Left: an invitation is the only way back (§3.10 D9), so it must still be created.
    [Fact]
    public async Task AMemberWhoLeft_CanStillBeInvited()
    {
        using var world = await World.BootstrapAsync("b35-left");
        using var member = await world.JoinAsync("b35-left-member", "viewer", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await member.Browser.Client.PostAsync("/me/leave", null)).StatusCode);

        var (response, _, _) = await world.InviteAsync(world.Owner, member.Email, "viewer", "assigned");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await InvitationsToAsync(world, member.Email));
    }

    // Test 11 survives the check above: it reads this organization's members, never the platform's people. A
    // person who is a member of ANOTHER organization is answered exactly as an address nobody holds.
    [Fact]
    public async Task Test11_AMemberOfAnotherOrganization_IsAnsweredAsAnUnknownAddressIs()
    {
        using var world = await World.BootstrapAsync("b35-t11");
        using var elsewhere = await World.BootstrapAsync("b35-t11-elsewhere");
        var nobody = World.Account("b35-t11-nobody").Email;

        var (toMember, _, _) = await world.InviteAsync(world.Owner, elsewhere.Owner.Email, "viewer", "assigned");
        var (toNobody, _, _) = await world.InviteAsync(world.Owner, nobody, "viewer", "assigned");

        Assert.Equal(HttpStatusCode.Created, toMember.StatusCode);
        Assert.Equal(toMember.StatusCode, toNobody.StatusCode);
        var a = await world.Owner.Browser.JsonAsync(toMember);
        var b = await world.Owner.Browser.JsonAsync(toNobody);
        Assert.Equal(a.EnumerateObject().Select(p => (p.Name, p.Value.ValueKind)), b.EnumerateObject().Select(p => (p.Name, p.Value.ValueKind)));
    }

    private static async Task AssertRefusedAsync(World world, HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await world.Owner.Browser.JsonAsync(response)).GetProperty("error").GetString());
    }

    private static Task<long> InvitationsToAsync(World world, string email) => Seed.CountAsMigratorAsync(
        "SELECT count(*) FROM invitations WHERE tenant_id = @t AND lower(btrim(email)) = lower(btrim(@e))",
        ("t", world.TenantId), ("e", email));
}

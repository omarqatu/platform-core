using System.Net;

namespace Conformance;

/// <summary>
/// The two invitation limits (OPEN_ITEMS 106), observable over HTTP: <c>429 too_many_attempts</c>.
/// <para>
/// Until invitations were delivered by e-mail this endpoint sent nothing and needed no limit. The change that
/// made it send is the one that closed the hole it opened — otherwise a holder of <c>core.members.manage</c>
/// could send unlimited mail to addresses of their choosing through the platform's own sending account.
/// </para>
/// <para>
/// Both numbers are marked assumptions <c>[—]</c> and settle at launch, like the login limiter's: per inviter
/// <c>[10]</c> in <c>[1 hour]</c>, per organization <c>[30]</c> in <c>[24 hours]</c>. The tests below are written
/// against the behaviour, not against the numbers where they can avoid it, so settling them does not rewrite the
/// file — only <see cref="PerInviter"/> and <see cref="PerTenant"/> move.
/// </para>
/// </summary>
public class InvitationLimitTests
{
    private const int PerInviter = 10;
    private const int PerTenant = 30;

    /// <summary>
    /// One inviter, past their window's allowance. The ceiling is per inviter because that is whose behaviour it
    /// bounds; the organization's own ceiling is the test below.
    /// </summary>
    [Fact]
    public async Task OneInviter_IsStoppedAtTheirOwnCeiling()
    {
        using var world = await World.BootstrapAsync("b04-per-inviter");
        var role = await world.CustomRoleAsync("core.members.manage");
        using var inviter = await world.JoinAsync("b04-inviter", role, "assigned");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < PerInviter + 1; i++)
        {
            var (response, _, _) = await world.InviteAsync(inviter, $"b04-pi-{i}-{World.Tag()}@b04.test", "operator", "assigned");
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(PerInviter, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
    }

    /// <summary>The refusal's code is the login limiter's: it says the same thing — wait, then try again.</summary>
    [Fact]
    public async Task TheRefusal_IsTooManyAttempts()
    {
        using var world = await World.BootstrapAsync("b04-code");
        var role = await world.CustomRoleAsync("core.members.manage");
        using var inviter = await world.JoinAsync("b04-code-inviter", role, "assigned");
        for (var i = 0; i < PerInviter; i++)
            await world.InviteAsync(inviter, $"b04-code-{i}-{World.Tag()}@b04.test", "operator", "assigned");

        var (refused, _, _) = await world.InviteAsync(inviter, $"b04-code-last-{World.Tag()}@b04.test", "operator", "assigned");

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("too_many_attempts", await refused.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The organization's ceiling, above the per-inviter one and not instead of it: a per-inviter window
    /// multiplies by the number of inviters, while the provider's quota is per sending account — production
    /// sends through one Gmail account shared by every tenant.
    /// <para>
    /// Four inviters sending eight each stay inside their own allowance of ten, so every refusal here is the
    /// organization's ceiling and not theirs. The assertion is the ceiling itself rather than the index of the
    /// first refusal: the memberships these inviters joined through created invitations of their own, which
    /// count — every invitation sent a message, whatever became of it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task TheOrganization_IsStoppedAtItsOwnCeiling_AboveEveryInviter()
    {
        using var world = await World.BootstrapAsync("b04-per-tenant");
        var role = await world.CustomRoleAsync("core.members.manage");
        var inviters = new List<Member>();
        for (var i = 0; i < 4; i++)
            inviters.Add(await world.JoinAsync($"b04-pt-{i}", role, "assigned"));

        var created = 0;
        var refused = 0;
        foreach (var inviter in inviters)
            for (var i = 0; i < 8; i++)
            {
                var (response, _, _) = await world.InviteAsync(inviter, $"b04-pt-{i}-{World.Tag()}@b04.test", "operator", "assigned");
                if (response.StatusCode == HttpStatusCode.Created) created++;
                else if (response.StatusCode == HttpStatusCode.TooManyRequests) refused++;
                else Assert.Fail($"unexpected {response.StatusCode}");
            }

        Assert.True(refused > 0, "the organization's ceiling never stopped anyone");
        // Every invitation of this organization counts, the four joins' own included, so the successes here are
        // at most the ceiling less those four.
        Assert.True(created <= PerTenant - inviters.Count,
            $"{created} created, which is past the ceiling of {PerTenant} less the {inviters.Count} joins");
        foreach (var inviter in inviters)
            Assert.True(await InvitationsByAsync(world, inviter) <= PerInviter, "an inviter passed their own ceiling");
    }

    /// <summary>
    /// The limit is the organization's, not the platform's: a second organization inviting at the same time is
    /// untouched. <c>invitations_tenant_read</c> is what scopes the count — the query names no tenant, because
    /// naming one would be the code filtering what the database filters (DEVELOPER_GUIDE §6).
    /// </summary>
    [Fact]
    public async Task AnotherOrganization_IsNotStoppedByThisOnes()
    {
        using var busy = await World.BootstrapAsync("b04-busy");
        using var quiet = await World.BootstrapAsync("b04-quiet");
        var role = await busy.CustomRoleAsync("core.members.manage");
        using var inviter = await busy.JoinAsync("b04-busy-inviter", role, "assigned");
        for (var i = 0; i < PerInviter + 1; i++)
            await busy.InviteAsync(inviter, $"b04-busy-{i}-{World.Tag()}@b04.test", "operator", "assigned");

        var (response, _, _) = await quiet.InviteAsync(quiet.Owner, $"b04-quiet-{World.Tag()}@b04.test", "operator", "assigned");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static Task<long> InvitationsByAsync(World world, Member inviter) =>
        Seed.CountAsMigratorAsync("SELECT count(*) FROM invitations WHERE tenant_id = @t AND invited_by = @u",
            ("t", world.TenantId), ("u", inviter.UserId));
}

using System.Net;
using System.Net.Http.Json;

namespace Conformance;

// PROOF_SPEC v1.3 T4.5 (categorical, §6) and T4.6 — the races of items a and g (PLATFORM_CORE v1.16 §3.10): two
// changes, each alone allowed, together leaving no active owner / no active 'all' membership. Both requests start at
// once behind one gate, in a fresh tenant each round; in every round exactly one succeeds and the other is refused
// (409), and exactly one active owner / 'all' membership remains — never both succeed.
public class T4_RaceTests
{
    private const int Rounds = 3;

    // ---- T4.5 — item a.

    [Fact]
    public Task T4_5_TwoDeparturesOfTheLastTwoOwners() => RoundsAsync("t4-5-leave", async world =>
    {
        using var second = await world.JoinAsync("owner2", "owner", "all");
        await RaceAsync(world.ActiveOwnersAsync,
            () => world.Owner.Browser.Client.PostAsync("/me/leave", null),
            () => second.Browser.Client.PostAsync("/me/leave", null));
    });

    [Fact]
    public Task T4_5_AManagerDisablingAnOwner_WhileTheOtherLeaves() => RoundsAsync("t4-5-mixed", async world =>
    {
        using var second = await world.JoinAsync("owner2", "owner", "all");
        using var manager = await world.JoinAsync("manager", "admin", "all");
        await RaceAsync(world.ActiveOwnersAsync,
            () => Status(manager, world.Owner, "disabled"),
            () => second.Browser.Client.PostAsync("/me/leave", null));
    });

    [Fact]
    public Task T4_5_TwoDisablingsOfTheLastTwoOwners() => RoundsAsync("t4-5-disable", async world =>
    {
        using var second = await world.JoinAsync("owner2", "owner", "all");
        using var m1 = await world.JoinAsync("m1", "admin", "all");
        using var m2 = await world.JoinAsync("m2", "admin", "all");
        await RaceAsync(world.ActiveOwnersAsync,
            () => Status(m1, world.Owner, "disabled"),
            () => Status(m2, second, "disabled"));
    });

    // ---- T4.6 — item g.

    [Fact]
    public Task T4_6_TwoDowngrades() => RoundsAsync("t4-6-down", async world =>
    {
        using var x = await world.JoinAsync("x", "admin", "all");
        await RaceAsync(world.ActiveAllAsync,
            () => Mode(world.Owner, x, "assigned"),
            () => Mode(x, world.Owner, "assigned"));
    });

    [Fact]
    public Task T4_6_AScopeManagerWithoutTheMembersPermission_DowngradingWhileAnotherDowngrades() => RoundsAsync("t4-6-scope", async world =>
    {
        using var x = await world.JoinAsync("x", "admin", "all");
        using var scopeManager = await world.JoinAsync("r", await world.CustomRoleAsync("core.scope.manage"), "assigned");
        await RaceAsync(world.ActiveAllAsync,
            () => Mode(scopeManager, world.Owner, "assigned"),
            () => Mode(world.Owner, x, "assigned"));
    });

    [Fact]
    public Task T4_6_ADisablingAndADowngrade() => RoundsAsync("t4-6-mixed", async world =>
    {
        using var x = await world.JoinAsync("x", "admin", "all");
        using var scopeManager = await world.JoinAsync("r", await world.CustomRoleAsync("core.scope.manage"), "assigned");
        await RaceAsync(world.ActiveAllAsync,
            () => Status(world.Owner, x, "disabled"),
            () => Mode(scopeManager, world.Owner, "assigned"));
    });

    // (Decided by the project owner in T4: item g covers departure too.) An 'all' member leaves while the other is
    // downgraded by a scope manager.
    [Fact]
    public Task T4_6_AnAllMemberLeaving_WhileAnotherIsDowngraded() => RoundsAsync("t4-6-leave", async world =>
    {
        using var x = await world.JoinAsync("x", "admin", "all");
        using var scopeManager = await world.JoinAsync("r", await world.CustomRoleAsync("core.scope.manage"), "assigned");
        await RaceAsync(world.ActiveAllAsync,
            () => x.Browser.Client.PostAsync("/me/leave", null),
            () => Mode(scopeManager, world.Owner, "assigned"));
    });

    [Fact]
    public Task T4_6_TwoDisablings() => RoundsAsync("t4-6-disable", async world =>
    {
        // A second owner with mode 'assigned', so item a never decides this race: only item g does.
        using var owner2 = await world.JoinAsync("owner2", "owner", "assigned");
        using var x = await world.JoinAsync("x", "admin", "all");
        using var m1 = await world.JoinAsync("m1", "admin", "assigned");
        using var m2 = await world.JoinAsync("m2", "admin", "assigned");
        await RaceAsync(world.ActiveAllAsync,
            () => Status(m1, world.Owner, "disabled"),
            () => Status(m2, x, "disabled"));
    });

    // ---- helpers

    private static async Task RoundsAsync(string label, Func<World, Task> round)
    {
        for (var i = 0; i < Rounds; i++)
        {
            using var world = await World.BootstrapAsync($"{label}-{i}");
            await round(world);
        }
    }

    private static async Task RaceAsync(Func<Task<long>> remaining, Func<Task<HttpResponseMessage>> a, Func<Task<HttpResponseMessage>> b)
    {
        Assert.Equal(2, await remaining());
        var statuses = await World.ConcurrentlyAsync(a, b);
        Assert.Equal([HttpStatusCode.NoContent, HttpStatusCode.Conflict], statuses.Order());
        Assert.Equal(1, await remaining());
    }

    private static Task<HttpResponseMessage> Status(Member by, Member target, string status) =>
        by.Browser.Client.PutAsJsonAsync($"/members/{target.MembershipId}/status", new { status });

    private static Task<HttpResponseMessage> Mode(Member by, Member target, string mode) =>
        by.Browser.Client.PutAsJsonAsync($"/scope/memberships/{target.MembershipId}/mode", new { scope_mode = mode });
}

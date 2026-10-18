using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Conformance;

// PLATFORM_CORE Amendment 02, stage (b) — renewing and cancelling (Amendment 01 §2.9; Amendment 02 §5.1). A renewal
// is a NEW row pointing at the one it renews; a cancellation is a status change; nothing is ever deleted. The
// database holds the shape: a composite FK on (tenant_id, renewed_from_id), a UNIQUE on the same pair, and
// subscriptions_status_gate, which lets no UPDATE reach a row that is not active.
public class T5_SubscriptionLifecycleTests
{
    private static async Task<object> BodyAsync(Guid client, string service, string endsOn) => new
    {
        client_id = client, service_name = service, ends_on = endsOn, category_id = await Seed.CategoryAsync("hosting"),
        provider = "Vendor", cost = "120.500", currency = "JOD", billing_cycle = "yearly", auto_renew = true,
        login_url = "https://vendor.example/login",
    };

    private static async Task<Guid> CreateAsync(World world, Guid client, string service = "VPS", string endsOn = "2027-06-01")
    {
        var created = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions", await BodyAsync(client, service, endsOn));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await world.Owner.Browser.JsonAsync(created)).GetProperty("id").GetGuid();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> RenewAsync(Browser by, Guid id, object body)
    {
        var response = await by.Client.PostAsJsonAsync($"/subscriptions/{id}/renew", body);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task<HttpResponseMessage> CancelAsync(Browser by, Guid id) =>
        by.Client.PostAsJsonAsync($"/subscriptions/{id}/cancel", new { });

    /// <summary>
    /// A renewal keeps every detail of the row it renews but its end date — above all its client, which is never
    /// read from the request — and the old row becomes 'renewed' rather than disappearing. Both rows stay.
    /// </summary>
    [Fact]
    public async Task Renewing_WritesANewRow_InheritsEverything_AndMarksTheOldOneRenewed()
    {
        using var world = await World.BootstrapAsync("a02b-renew");
        var p = await world.ClientAsync("P");
        var previous = await CreateAsync(world, p);

        var (status, created) = await RenewAsync(world.Owner.Browser, previous, new { ends_on = "2028-06-01" });

        Assert.Equal(HttpStatusCode.Created, status);
        var renewal = created.GetProperty("id").GetGuid();
        Assert.NotEqual(previous, renewal);

        var (_, fresh) = await world.Owner.Browser.GetAsync($"/subscriptions/{renewal}");
        Assert.Equal(p, fresh.GetProperty("client_id").GetGuid());                     // inherited, never from the request
        Assert.Equal(previous, fresh.GetProperty("renewed_from_id").GetGuid());
        Assert.Equal("active", fresh.GetProperty("status").GetString());
        Assert.Equal("2028-06-01", fresh.GetProperty("ends_on").GetString());
        Assert.Equal("VPS", fresh.GetProperty("service_name").GetString());
        Assert.Equal("120.500", fresh.GetProperty("cost").GetString());
        Assert.Equal("JOD", fresh.GetProperty("currency").GetString());
        Assert.Equal("Vendor", fresh.GetProperty("provider").GetString());
        Assert.Equal("yearly", fresh.GetProperty("billing_cycle").GetString());
        Assert.True(fresh.GetProperty("auto_renew").GetBoolean());
        Assert.Equal("https://vendor.example/login", fresh.GetProperty("login_url").GetString());

        // The old row is kept, marked, and keeps its own end date: a renewal is not an extension.
        var (_, old) = await world.Owner.Browser.GetAsync($"/subscriptions/{previous}");
        Assert.Equal("renewed", old.GetProperty("status").GetString());
        Assert.Equal("2027-06-01", old.GetProperty("ends_on").GetString());
        Assert.Equal(2, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE scope_ref_id = @c", ("c", p)));
    }

    /// <summary>
    /// A renewal that names a client is refused, whichever client it names, and nothing is written. Stage (a)
    /// refused the same thing on the edit; this is the second door to the same room — moving a subscription between
    /// clients is a scope crossing inside the tenant, and the renewal is where it would be easiest to miss.
    /// </summary>
    [Fact]
    public async Task RenewingThatNamesAClient_IsRefused_AndTheRenewalKeepsTheOriginalsClient()
    {
        using var world = await World.BootstrapAsync("a02b-renew-move");
        var p = await world.ClientAsync("P");
        var q = await world.ClientAsync("Q");
        var previous = await CreateAsync(world, p);

        foreach (var named in new[] { q, p })   // another client, and the same one: both refused, for being named
        {
            var (status, body) = await RenewAsync(world.Owner.Browser, previous, new { ends_on = "2028-06-01", client_id = named });
            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Equal("invalid_value", body.GetProperty("error").GetString());
        }

        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE tenant_id = @t", ("t", world.TenantId)));
        Assert.Equal("active", await Seed.ScalarStringAsMigratorAsync("SELECT status FROM subscriptions WHERE id = @i", ("i", previous)));

        // And the renewal that does not name one keeps the original's client.
        var (ok, created) = await RenewAsync(world.Owner.Browser, previous, new { ends_on = "2028-06-01" });
        Assert.Equal(HttpStatusCode.Created, ok);
        Assert.Equal(p, await World.ScalarAsync("SELECT scope_ref_id FROM subscriptions WHERE id = @i", ("i", created.GetProperty("id").GetGuid())));
    }

    // A renewal runs past the term it renews: the same date or an earlier one is a typo, not a renewal.
    [Theory]
    [InlineData("2027-06-01")]
    [InlineData("2027-05-31")]
    public async Task RenewingToADateThatIsNotLater_IsRefused(string endsOn)
    {
        using var world = await World.BootstrapAsync("a02b-renew-date");
        var previous = await CreateAsync(world, await world.ClientAsync("P"));

        var (status, body) = await RenewAsync(world.Owner.Browser, previous, new { ends_on = endsOn });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("invalid_value", body.GetProperty("error").GetString());
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE tenant_id = @t", ("t", world.TenantId)));
    }

    // Cancelling is a status change and nothing else: the row, its dates and its client stay, and the count does
    // not fall. No DELETE, here or anywhere in the module.
    [Fact]
    public async Task Cancelling_ChangesTheStatusAndKeepsTheRow()
    {
        using var world = await World.BootstrapAsync("a02b-cancel");
        var p = await world.ClientAsync("P");
        var id = await CreateAsync(world, p);

        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(world.Owner.Browser, id)).StatusCode);

        var (_, s) = await world.Owner.Browser.GetAsync($"/subscriptions/{id}");
        Assert.Equal("cancelled", s.GetProperty("status").GetString());
        Assert.Equal("2027-06-01", s.GetProperty("ends_on").GetString());
        Assert.Equal(p, s.GetProperty("client_id").GetGuid());
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE tenant_id = @t", ("t", world.TenantId)));
        // Still listed: cancelled is a state, not a disappearance.
        Assert.Equal(1, (await world.Owner.Browser.GetAsync("/subscriptions?limit=200")).Body.GetProperty("visible_count").GetInt64());
    }

    /// <summary>
    /// Once a subscription is cancelled or renewed nothing reaches it again — not an edit, not a cancellation, not
    /// a second renewal. The code answers 409 subscription_not_active, and beneath it
    /// <c>subscriptions_status_gate</c> refuses the UPDATE outright, so removing the check would not open the door.
    /// </summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("renew")]
    public async Task OnceItHasLeftActive_NothingReachesItAgain(string how)
    {
        using var world = await World.BootstrapAsync($"a02b-gate-{how}");
        var p = await world.ClientAsync("P");
        var id = await CreateAsync(world, p);
        if (how == "cancel")
            Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(world.Owner.Browser, id)).StatusCode);
        else
            Assert.Equal(HttpStatusCode.Created, (await RenewAsync(world.Owner.Browser, id, new { ends_on = "2028-06-01" })).Status);
        var expected = how == "cancel" ? "cancelled" : "renewed";

        var (cancelled, cancelBody) = (await CancelAsync(world.Owner.Browser, id)) is var r
            ? (r.StatusCode, await r.Content.ReadAsStringAsync()) : default;
        var (renewed, renewBody) = await RenewAsync(world.Owner.Browser, id, new { ends_on = "2029-06-01" });
        var (edited, editBody) = await world.Owner.Browser.PutAsync($"/subscriptions/{id}", new
        {
            service_name = "edited", ends_on = "2029-01-01", category_id = await Seed.CategoryAsync("other"),
            billing_cycle = "yearly", auto_renew = false,
        });

        Assert.Equal(HttpStatusCode.Conflict, cancelled);
        Assert.Contains("subscription_not_active", cancelBody);
        Assert.Equal(HttpStatusCode.Conflict, renewed);
        Assert.Equal("subscription_not_active", renewBody.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Conflict, edited);
        Assert.Contains("subscription_not_active", editBody);
        Assert.Equal(expected, await Seed.ScalarStringAsMigratorAsync("SELECT status FROM subscriptions WHERE id = @i", ("i", id)));
        Assert.Equal("VPS", await Seed.ScalarStringAsMigratorAsync("SELECT service_name FROM subscriptions WHERE id = @i", ("i", id)));
    }

    /// <summary>
    /// The gate beneath the code (Amendment 02 §5.1): with the API out of the way, a member with full scope cannot
    /// update a row that has left 'active' — the UPDATE reaches no row. Removing the endpoint's check would not
    /// make it possible; the database is what refuses.
    /// </summary>
    [Fact]
    public async Task TheStatusGate_RefusesTheUpdateBeneathTheApi()
    {
        using var world = await World.BootstrapAsync("a02b-gate-sql");
        var p = await world.ClientAsync("P");
        var id = await CreateAsync(world, p);
        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(world.Owner.Browser, id)).StatusCode);

        await using var session = await Session.OpenAsync(Target.AppUser, tenant: world.TenantId, scopeAll: true);

        Assert.Equal(0, await session.ExecuteAsync("UPDATE subscriptions SET service_name = 'beneath' WHERE id = @i", ("i", id)));
        Assert.Equal(0, await session.ExecuteAsync("UPDATE subscriptions SET status = 'active' WHERE id = @i", ("i", id)));
        // And an active row in the same session is still writable: the gate narrows, it does not close.
        var active = await CreateAsync(world, p, "Second", "2027-07-01");
        Assert.Equal(1, await session.ExecuteAsync("UPDATE subscriptions SET service_name = 'beneath' WHERE id = @i", ("i", active)));
    }

    /// <summary>
    /// One subscription is renewed once, and the database is what says so: with the endpoint's check bypassed, a
    /// second row pointing at the same predecessor violates UNIQUE (tenant_id, renewed_from_id).
    /// </summary>
    [Fact]
    public async Task ASecondRenewalOfTheSamePredecessor_IsRefusedByTheDatabase()
    {
        using var world = await World.BootstrapAsync("a02b-renew-once");
        var p = await world.ClientAsync("P");
        var previous = await CreateAsync(world, p);
        Assert.Equal(HttpStatusCode.Created, (await RenewAsync(world.Owner.Browser, previous, new { ends_on = "2028-06-01" })).Status);

        await using var session = await Session.OpenAsync(Target.AppUser, tenant: world.TenantId, scopeAll: true);
        var state = await session.SqlStateOfAsync(
            "INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at, reminder_type, category_id, " +
            "billing_cycle, auto_renew, status, renewed_from_id) " +
            "VALUES (uuidv7(), @t, @c, 'second renewal', DATE '2029-06-01', now(), 'subscription', " +
            "(SELECT id FROM categories WHERE code = 'other'), 'yearly', false, 'active', @p)",
            ("t", world.TenantId), ("c", p), ("p", previous));

        Assert.Equal("23505", state);   // unique_violation → 409 conflict through ErrorResponses
    }

    /// <summary>
    /// The renewal's link is a composite FK on (tenant_id, renewed_from_id) — §3.3, as every FK between two
    /// tenant_id tables. A renewal pointing at another tenant's subscription is refused by the key itself, not by
    /// any code: the pair never matches.
    /// </summary>
    [Fact]
    public async Task TheRenewalLink_IsCompositeOnTenant()
    {
        using var mine = await World.BootstrapAsync("a02b-fk-mine");
        using var theirs = await World.BootstrapAsync("a02b-fk-theirs");
        var p = await mine.ClientAsync("P");
        var elsewhere = await CreateAsync(theirs, await theirs.ClientAsync("Theirs"));

        await using var session = await Session.OpenAsync(Target.AppUser, tenant: mine.TenantId, scopeAll: true);
        var state = await session.SqlStateOfAsync(
            "INSERT INTO subscriptions (id, tenant_id, scope_ref_id, service_name, ends_on, created_at, reminder_type, category_id, " +
            "billing_cycle, auto_renew, status, renewed_from_id) " +
            "VALUES (uuidv7(), @t, @c, 'across', DATE '2029-06-01', now(), 'subscription', " +
            "(SELECT id FROM categories WHERE code = 'other'), 'yearly', false, 'active', @p)",
            ("t", mine.TenantId), ("c", p), ("p", elsewhere));

        Assert.Equal("23503", state);   // foreign_key_violation → 400 invalid_reference
    }

    // Across scope: an assigned member renews and cancels the subscriptions of their own clients, and no others.
    [Fact]
    public async Task AcrossScope_AnAssignedMemberRenewsAndCancelsOnlyTheirOwn()
    {
        using var world = await World.BootstrapAsync("a02b-life-scope");
        var mineClient = await world.ClientAsync("Mine");
        var theirsClient = await world.ClientAsync("Theirs");
        var mineSub = await CreateAsync(world, mineClient, "Mine");
        var theirsSub = await CreateAsync(world, theirsClient, "Theirs");
        using var assigned = await world.JoinAsync("a02b-life-op", "operator", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{mineClient}", new { active = true, reason = "a02b" })).Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await RenewAsync(assigned.Browser, theirsSub, new { ends_on = "2028-06-01" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(assigned.Browser, theirsSub)).StatusCode);
        Assert.Equal("active", await Seed.ScalarStringAsMigratorAsync("SELECT status FROM subscriptions WHERE id = @i", ("i", theirsSub)));

        var (status, created) = await RenewAsync(assigned.Browser, mineSub, new { ends_on = "2028-06-01" });
        Assert.Equal(HttpStatusCode.Created, status);
        // The renewal lands inside the member's own scope, because it inherited the client it renews.
        Assert.Equal(mineClient, await World.ScalarAsync("SELECT scope_ref_id FROM subscriptions WHERE id = @i",
            ("i", created.GetProperty("id").GetGuid())));
    }

    // Across tenants: another tenant's subscription is neither renewed nor cancelled, and nothing about it changes.
    [Fact]
    public async Task AcrossTenants_AnotherTenantsSubscriptionIsUntouched()
    {
        using var mine = await World.BootstrapAsync("a02b-life-mine");
        using var theirs = await World.BootstrapAsync("a02b-life-theirs");
        var id = await CreateAsync(theirs, await theirs.ClientAsync("Theirs"));

        Assert.Equal(HttpStatusCode.Forbidden, (await RenewAsync(mine.Owner.Browser, id, new { ends_on = "2028-06-01" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(mine.Owner.Browser, id)).StatusCode);

        Assert.Equal("active", await Seed.ScalarStringAsMigratorAsync("SELECT status FROM subscriptions WHERE id = @i", ("i", id)));
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM subscriptions WHERE tenant_id = @t", ("t", theirs.TenantId)));
    }

    // Renewing and cancelling need subscriptions.write, which a viewer does not hold (Amendment 01 §4.3: the
    // existing permission covers both, and no new one was added).
    [Fact]
    public async Task AViewerRenewsAndCancelsNothing()
    {
        using var world = await World.BootstrapAsync("a02b-viewer");
        var id = await CreateAsync(world, await world.ClientAsync("P"));
        using var viewer = await world.JoinAsync("a02b-viewer-v", "viewer", "all");

        Assert.Equal(HttpStatusCode.Forbidden, (await RenewAsync(viewer.Browser, id, new { ends_on = "2028-06-01" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CancelAsync(viewer.Browser, id)).StatusCode);
        Assert.Equal("active", await Seed.ScalarStringAsMigratorAsync("SELECT status FROM subscriptions WHERE id = @i", ("i", id)));
    }
}

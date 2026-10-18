using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Conformance;

// PLATFORM_CORE Amendment 02, stage (b) — the client's own columns and the subscription's lifecycle (Amendment 01
// §2.8, §2.9, §4.2, §5.1; Amendment 02 §5.1). A client is the scoped entity, so every behaviour here is proven
// across tenants and across scope, not only against one member. What the API commits happens in a tenant of the
// test's own (World); the seed tenants are read, never written through the API.
public class T5_ClientLifecycleTests
{
    private static object ClientBody(string name, string? email = null, string? phone = null, string? telegram = null) =>
        new { name, email, phone_e164 = phone, telegram_chat_id = telegram };

    // ---- Creating a client, and the one place the second axis narrows the permission.

    // An operator whose scope mode is 'all' holds subscriptions.clients.write and creates: the row is theirs, it is
    // active, and it carries no channel it was not given.
    [Fact]
    public async Task OperatorWithScopeAll_CreatesAClient_ActiveAndWithTheChannelsGiven()
    {
        using var world = await World.BootstrapAsync("a02b-create");
        using var operatorAll = await world.JoinAsync("a02b-create-op", "operator", "all");

        var created = await operatorAll.Browser.Client.PostAsJsonAsync("/subscriptions/clients",
            ClientBody("Nour Co", "nour@example.com", "+962791234567", "-1001234567890"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await operatorAll.Browser.JsonAsync(created)).GetProperty("id").GetGuid();

        var (status, client) = await operatorAll.Browser.GetAsync($"/subscriptions/clients/{id}");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Nour Co", client.GetProperty("name").GetString());
        Assert.Equal("nour@example.com", client.GetProperty("email").GetString());
        Assert.Equal("+962791234567", client.GetProperty("phone_e164").GetString());
        Assert.Equal("-1001234567890", client.GetProperty("telegram_chat_id").GetString());
        Assert.True(client.GetProperty("is_active").GetBoolean());
        // The synonym (0008): a client's scope_ref_id is its own id, written by the application, held by a CHECK.
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM clients WHERE id = @i AND scope_ref_id = @i AND tenant_id = @t", ("i", id), ("t", world.TenantId)));
    }

    /// <summary>
    /// The second axis narrows the permission at creation, and only there. A client is its own scoped entity, so a
    /// new row has no assignment by virtue of being new and <c>client_scope</c>'s WITH CHECK refuses the insert
    /// (42501 → 403 not_permitted) from a member in mode 'assigned' — holder of the permission or not. The same
    /// member edits the clients assigned to them. Amendment 01 §4.2 states this beside the permission; this is what
    /// states it in the running system.
    /// </summary>
    [Fact]
    public async Task OperatorAssigned_CannotCreateAClient_ButEditsTheOnesAssignedToThem()
    {
        using var world = await World.BootstrapAsync("a02b-assigned");
        var p = await world.ClientAsync("P");
        using var assigned = await world.JoinAsync("a02b-assigned-op", "operator", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{p}", new { active = true, reason = "a02b" })).Status);
        var before = await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE tenant_id = @t", ("t", world.TenantId));

        var created = await assigned.Browser.Client.PostAsJsonAsync("/subscriptions/clients", ClientBody("Refused"));

        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);
        Assert.Contains("not_permitted", await created.Content.ReadAsStringAsync());
        Assert.Equal(before, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE tenant_id = @t", ("t", world.TenantId)));

        // The same member, the same permission, a client they are assigned: the edit lands.
        Assert.Equal(HttpStatusCode.NoContent, (await assigned.Browser.PutAsync($"/subscriptions/clients/{p}", ClientBody("P renamed"))).Status);
        Assert.Equal("P renamed", await Seed.ScalarStringAsMigratorAsync("SELECT name FROM clients WHERE id = @i", ("i", p)));
    }

    // ---- Across scope: an assigned member sees and edits their own clients and no others.
    [Fact]
    public async Task AcrossScope_AnAssignedMemberSeesAndEditsOnlyTheirOwnClients()
    {
        using var world = await World.BootstrapAsync("a02b-scope");
        var mine = await world.ClientAsync("Mine");
        var theirs = await world.ClientAsync("Theirs");
        using var assigned = await world.JoinAsync("a02b-scope-op", "operator", "assigned");
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{assigned.MembershipId}/assignments/{mine}", new { active = true, reason = "a02b" })).Status);

        var (_, list) = await assigned.Browser.GetAsync("/subscriptions/clients?limit=200");
        Assert.Equal("assigned", list.GetProperty("scope_mode").GetString());
        Assert.Equal([mine], list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));

        // Unassigned: invisible to read and to write, and the two answer alike.
        var (read, _) = await assigned.Browser.GetAsync($"/subscriptions/clients/{theirs}");
        Assert.Equal(HttpStatusCode.Forbidden, read);
        Assert.Equal(HttpStatusCode.Forbidden, (await assigned.Browser.PutAsync($"/subscriptions/clients/{theirs}", ClientBody("stolen"))).Status);
        Assert.Equal("Theirs", await Seed.ScalarStringAsMigratorAsync("SELECT name FROM clients WHERE id = @i", ("i", theirs)));
    }

    // ---- Across tenants: another tenant's client is not readable, not editable and not disableable, and nothing
    // about it changes. Two worlds, two owners — each with full scope in their own.
    [Fact]
    public async Task AcrossTenants_AnotherTenantsClientIsNeitherReadNorWritten()
    {
        using var mine = await World.BootstrapAsync("a02b-mine");
        using var theirs = await World.BootstrapAsync("a02b-theirs");
        var client = await theirs.ClientAsync("Theirs");

        Assert.Equal(HttpStatusCode.Forbidden, (await mine.Owner.Browser.GetAsync($"/subscriptions/clients/{client}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await mine.Owner.Browser.PutAsync($"/subscriptions/clients/{client}", ClientBody("stolen"))).Status);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await mine.Owner.Browser.PutAsync($"/subscriptions/clients/{client}/status", new { is_active = false })).Status);
        Assert.Equal(1, await Seed.CountAsMigratorAsync(
            "SELECT count(*) FROM clients WHERE id = @i AND name = 'Theirs' AND is_active", ("i", client)));
    }

    // ---- Disabling: the permission, and what it does not touch.

    /// <summary>
    /// <c>subscriptions.clients.manage</c> is owner and admin, never operator (Amendment 01 §4.2) — and the
    /// endpoint is what refuses, not the screen. An operator with scope 'all', who may create and edit a client,
    /// may not disable one; the row stays active.
    /// </summary>
    [Fact]
    public async Task Disabling_IsRefusedToAnOperator_ByTheEndpoint()
    {
        using var world = await World.BootstrapAsync("a02b-manage");
        var p = await world.ClientAsync("P");
        using var operatorAll = await world.JoinAsync("a02b-manage-op", "operator", "all");

        // The same member, the same client: the edit lands and the disabling does not. One permission apart.
        Assert.Equal(HttpStatusCode.NoContent, (await operatorAll.Browser.PutAsync($"/subscriptions/clients/{p}", ClientBody("P edited"))).Status);
        var (status, body) = await operatorAll.Browser.PutAsync($"/subscriptions/clients/{p}/status", new { is_active = false });

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Contains("not_permitted", body);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE id = @i AND is_active", ("i", p)));
    }

    /// <summary>
    /// Disabling a client stops its reminders and changes nothing about its subscriptions: their status, their
    /// dates, their count and their history stay exactly as they are (the project owner's decision, 2026-10-17).
    /// Re-enabling resumes them, with no catch-up for what was missed — there is nothing to catch up on, because
    /// nothing was changed. This is the test that would fail if a cascade were ever added.
    /// </summary>
    [Fact]
    public async Task Disabling_StopsNothingButTheReminders_AndCascadesToNoSubscription()
    {
        using var world = await World.BootstrapAsync("a02b-disable");
        var p = await world.ClientAsync("P");
        await world.SubscriptionsAsync(p, new DateOnly(2027, 3, 1), new DateOnly(2027, 9, 1));
        using var admin = await world.JoinAsync("a02b-disable-admin", "admin", "all");
        const string Shape = "SELECT count(*) FROM subscriptions WHERE scope_ref_id = @c AND status = 'active' AND renewed_from_id IS NULL";
        Assert.Equal(2, await Seed.CountAsMigratorAsync(Shape, ("c", p)));

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.PutAsync($"/subscriptions/clients/{p}/status", new { is_active = false })).Status);

        Assert.Equal(0, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE id = @i AND is_active", ("i", p)));
        Assert.Equal(2, await Seed.CountAsMigratorAsync(Shape, ("c", p)));
        // A disabled client is still listed — disabling is not deleting — and its subscriptions are still listed too.
        var (_, clients) = await admin.Browser.GetAsync("/subscriptions/clients?limit=200");
        Assert.Contains(clients.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == p && !x.GetProperty("is_active").GetBoolean());
        Assert.Equal(2, (await admin.Browser.GetAsync("/subscriptions?limit=200")).Body.GetProperty("visible_count").GetInt64());

        // A disabled client still gains subscriptions: disabling stops the sending, not the entry.
        var added = await admin.Browser.Client.PostAsJsonAsync("/subscriptions", new
        {
            client_id = p, service_name = "added while disabled", ends_on = "2027-12-01",
            category_id = await Seed.CategoryAsync("other"), billing_cycle = "yearly", auto_renew = false,
        });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        // And enabling again restores exactly what disabling changed, and nothing else.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.Browser.PutAsync($"/subscriptions/clients/{p}/status", new { is_active = true })).Status);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE id = @i AND is_active", ("i", p)));
        Assert.Equal(3, await Seed.CountAsMigratorAsync(Shape, ("c", p)));
    }

    // Setting the value a client already has writes nothing and answers 204: EF issues no UPDATE for an unchanged
    // row, so the request is idempotent and earns no audit entry for a change that did not happen. Stated here
    // because a later move to a rows-affected guard on this write would turn the second call into a 403.
    [Fact]
    public async Task SettingTheStatusAClientAlreadyHas_WritesNothingAndIsNotAnError()
    {
        using var world = await World.BootstrapAsync("a02b-idempotent");
        var p = await world.ClientAsync("P");
        const string Entries = "SELECT count(*) FROM audit_log WHERE tenant_id = @t AND entity_type = 'clients' AND entity_id = @i";
        var before = await Seed.CountAsMigratorAsync(Entries, ("t", world.TenantId), ("i", p));

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.NoContent,
                (await world.Owner.Browser.PutAsync($"/subscriptions/clients/{p}/status", new { is_active = true })).Status);

        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE id = @i AND is_active", ("i", p)));
        Assert.Equal(before, await Seed.CountAsMigratorAsync(Entries, ("t", world.TenantId), ("i", p)));
    }

    // Editing a client never disables it: is_active is not a field of the edit, so a request that carries one
    // changes nothing. The two acts are two permissions and must stay two doors.
    [Fact]
    public async Task EditingAClient_CannotDisableIt_WhateverTheRequestCarries()
    {
        using var world = await World.BootstrapAsync("a02b-noflag");
        var p = await world.ClientAsync("P");
        using var operatorAll = await world.JoinAsync("a02b-noflag-op", "operator", "all");

        var (status, _) = await operatorAll.Browser.PutAsync($"/subscriptions/clients/{p}",
            new { name = "P", email = (string?)null, phone_e164 = (string?)null, telegram_chat_id = (string?)null, is_active = false });

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE id = @i AND is_active", ("i", p)));
    }

    // ---- The client's own rules: the shapes the database holds as CHECKs, refused before the write.
    [Theory]
    [InlineData("", null, null, null)]                                  // a client has a name
    [InlineData("P", "not-an-address", null, null)]
    [InlineData("P", "", null, null)]                                   // '' is not "no channel": it matches nothing
    [InlineData("P", null, "0791234567", null)]                         // E.164 or nothing: no local spelling
    [InlineData("P", null, "+0791234567", null)]                        // no leading zero after the +
    [InlineData("P", null, "+٩٦٢٧٩١٢٣٤٥٦٧", null)]                      // Arabic-Indic digits are not digits here
    [InlineData("P", null, null, "not-a-number")]
    public async Task TheClientsRules_RefuseBeforeTheWrite(string name, string? email, string? phone, string? telegram)
    {
        using var world = await World.BootstrapAsync("a02b-rules");
        var before = await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE tenant_id = @t", ("t", world.TenantId));

        var created = await world.Owner.Browser.Client.PostAsJsonAsync("/subscriptions/clients", ClientBody(name, email, phone, telegram));

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("invalid_value", await created.Content.ReadAsStringAsync());
        Assert.Equal(before, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE tenant_id = @t", ("t", world.TenantId)));
    }

    // No role gains DELETE on clients in this stage, and the module deletes nothing: disable, never delete
    // (Amendment 01 §2.8, §2.9). app_user's DELETE is v1.16 §3.8's table-wide grant and stays as it was — the guard
    // is that no path uses it. There is no endpoint to call, so this is what says so.
    [Fact]
    public async Task NoEndpointDeletesAClient()
    {
        using var world = await World.BootstrapAsync("a02b-nodelete");
        var p = await world.ClientAsync("P");

        var deleted = await world.Owner.Browser.Client.DeleteAsync($"/subscriptions/clients/{p}");

        // 405, not 404: the path exists — for reading and editing — and DELETE is not among what it answers.
        Assert.Equal(HttpStatusCode.MethodNotAllowed, deleted.StatusCode);
        Assert.Equal(1, await Seed.CountAsMigratorAsync("SELECT count(*) FROM clients WHERE id = @i", ("i", p)));
    }
}

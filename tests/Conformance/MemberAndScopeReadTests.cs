using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Conformance;

/// <summary>
/// The four read surfaces the member and scope screens need (group ب, OPEN_ITEMS 100). Not a PROOF_SPEC criterion:
/// the write endpoints of §3.10 and §4.8 have existed since T4, and nothing could list what they act on, so the
/// second axis had no interface at all. Approved by the project owner.
/// <para>
/// Each one is two layers, like every management surface: the explicit permission at the endpoint (5), and RLS
/// beneath it. The permissions were chosen by the project owner — the member and role catalogues answer to
/// <c>core.members.manage</c> OR <c>core.scope.manage</c> (whoever assigns must see whom they assign to), the
/// invitation list to <c>core.members.manage</c> alone, and a colleague's scope to <c>core.scope.manage</c> alone.
/// </para>
/// <para>
/// The last one is not a preference. §4.8, 1.8: "someone who sees every file has no corresponding need to see who
/// was assigned to what" — so <c>membership_scope_read</c> and <c>scope_assignment_read</c> admit a colleague's row
/// only under <c>can_manage_scope</c>. A member manager who does not manage scope reads NULL for every colleague's
/// mode and zero assignment rows, and the responses carry that absence rather than inventing a value.
/// </para>
/// <para>
/// PROOF_SPEC T8.2 for member data: an assigned member must not learn the organization's size. The database does not
/// hold that line — <c>membership_tenant_read</c> admits every membership of the tenant to any member, measured on
/// PostgreSQL 18.6 before these endpoints were written — so the endpoint's permission check is the whole of it, and
/// the tests below are what hold it. (The asymmetry itself is OPEN_ITEMS 103, for the project owner after this group.)
/// </para>
/// </summary>
public class MemberAndScopeReadTests
{
    // ---- GET /roles — the tenant's role catalogue, for the invitation form and the role screen.

    /// <summary>Exactly the tenant's own roles, as the database holds them — names included, since the interface
    /// shows a role's name from the row (name_ar/name_en), the way it shows a category's.</summary>
    [Fact]
    public async Task Roles_AreExactlyTheActiveTenantsOwn()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);

        var (status, body) = await omar.GetAsync("/roles");

        Assert.Equal(HttpStatusCode.OK, status);
        var codes = body.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("code").GetString()!).ToList();
        Assert.Equal(await RoleCodesOfAsync(Seed.AlAmin), codes.Order(StringComparer.Ordinal).ToList());
        var first = body.GetProperty("items").EnumerateArray().First();
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("name_ar").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(first.GetProperty("name_en").GetString()));
    }

    /// <summary>Whoever manages scope alone still needs the catalogue: Rami holds core.scope.manage and not
    /// core.members.manage (the seed's scope-manager role beside operator).</summary>
    [Fact]
    public async Task Roles_AreReadableByWhoeverManagesScopeAlone()
    {
        using var rami = await Browser.SignedInAsync("rami", Seed.AlAmin);

        var (status, _) = await rami.GetAsync("/roles");

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Theory]
    [InlineData("khaled")]   // operator, scope assigned — manages neither
    [InlineData("layla")]    // viewer, scope all — manages neither
    public async Task Roles_AreRefusedToWhoeverManagesNeither(string username)
    {
        using var browser = await Browser.SignedInAsync(username, Seed.AlAmin);

        var (status, body) = await browser.GetAsync("/roles");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("not_permitted", body.GetProperty("error").GetString());
    }

    // ---- GET /members — who is in the organization.

    [Fact]
    public async Task Members_AreEveryMembershipOfTheActiveTenant_WithTheirRoles()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);

        var (status, body) = await omar.GetAsync("/members?limit=200");

        Assert.Equal(HttpStatusCode.OK, status);
        var names = body.GetProperty("items").EnumerateArray()
            .Select(m => m.GetProperty("full_name").GetString()!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(["Khaled", "Layla", "Omar", "Rami", "Sara"], names);

        var rami = body.GetProperty("items").EnumerateArray().Single(m => m.GetProperty("username").GetString() == "rami");
        Assert.Equal(["operator", "scope-manager"],
            rami.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString()!).Order(StringComparer.Ordinal).ToList());
        Assert.Equal("active", rami.GetProperty("status").GetString());
    }

    /// <summary>
    /// T8.2 for member data: an assigned operator and a viewer are refused outright, so the organization's size never
    /// reaches them — not as a count, not as an empty list, not as a page of their own row. The database would have
    /// shown them all five rows; this check is the only thing between.
    /// </summary>
    [Theory]
    [InlineData("khaled")]
    [InlineData("layla")]
    public async Task Members_AreRefusedToWhoeverManagesNeither_AndLeakNoCount(string username)
    {
        using var browser = await Browser.SignedInAsync(username, Seed.AlAmin);

        var (status, body) = await browser.GetAsync("/members?limit=200");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("not_permitted", body.GetProperty("error").GetString());
        Assert.False(body.TryGetProperty("visible_count", out _));
        Assert.False(body.TryGetProperty("total_count", out _));
        Assert.False(body.TryGetProperty("items", out _));
    }

    /// <summary>
    /// A person who belongs to two organizations appears once in each — not twice in either.
    /// <para>
    /// Two permissive policies OR together on <c>memberships</c>: <c>membership_tenant_read</c> (this tenant's) and
    /// <c>membership_self</c> (the caller's own, in <b>every</b> tenant — which is how GET /tenants lists the
    /// organizations a person belongs to, T3.7). A list built on the table alone therefore shows the caller their
    /// own membership in every other organization, inside this one's member list. Omar is in Al-Amin and in Maan,
    /// so the seed already contained the case; the first run of this file is what found it.
    /// </para>
    /// <para>
    /// Not an isolation failure — no other tenant's people appear, only the caller's own other row — which is why
    /// the endpoint's <c>tenant_id</c> constraint is the list's subject and not a filter standing in for RLS
    /// (LastMemberGuards constrains the same table the same way, in SQL, for the same reason).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Members_DoNotCarryTheCallersMembershipsInOtherOrganizations()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);

        var (status, body) = await omar.GetAsync("/members?limit=200");

        Assert.Equal(HttpStatusCode.OK, status);
        var omarRows = body.GetProperty("items").EnumerateArray()
            .Where(m => m.GetProperty("username").GetString() == "omar").ToList();
        Assert.Single(omarRows);
        Assert.Equal(await Seed.MembershipAsync("omar", Seed.AlAmin), omarRows[0].GetProperty("membership_id").GetGuid());
        Assert.True(omarRows[0].GetProperty("is_self").GetBoolean());
    }

    /// <summary>The scope declaration, like every aggregate surface (3.5, 4.8; §14's rule for lists).</summary>
    [Fact]
    public async Task Members_CarryTheScopeDeclaration()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);

        var (status, body) = await omar.GetAsync("/members?limit=200");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("all", body.GetProperty("scope_mode").GetString());
        Assert.Equal(5, body.GetProperty("visible_count").GetInt64());
        Assert.Equal(5, body.GetProperty("total_count").GetInt64());
        Assert.False(body.GetProperty("has_more_in_scope").GetBoolean());
    }

    /// <summary>
    /// §4.8's narrowing, carried up to the response: a member manager who does not manage scope reads NULL for every
    /// colleague's mode — and a real value for their own row, which membership_scope_read admits by membership_id.
    /// The endpoint computes nothing; this is RLS's own answer, projected. The interface renders no column at all
    /// from it (the project owner's decision), never an empty one.
    /// </summary>
    [Fact]
    public async Task Members_ScopeModeIsNullForColleagues_ToWhoeverDoesNotManageScope()
    {
        using var world = await World.BootstrapAsync("b01-members-only");
        var role = await world.CustomRoleAsync("core.members.manage");
        using var manager = await world.JoinAsync("b01-mm", role, "all");

        var (status, body) = await manager.Browser.GetAsync("/members?limit=200");

        Assert.Equal(HttpStatusCode.OK, status);
        var rows = body.GetProperty("items").EnumerateArray().ToList();
        var own = rows.Single(m => m.GetProperty("membership_id").GetGuid() == manager.MembershipId);
        var owner = rows.Single(m => m.GetProperty("membership_id").GetGuid() == world.Owner.MembershipId);
        Assert.Equal("all", own.GetProperty("scope_mode").GetString());
        Assert.Equal(JsonValueKind.Null, owner.GetProperty("scope_mode").ValueKind);
    }

    /// <summary>The same read under core.scope.manage sees every mode — the other half of the narrowing.</summary>
    [Fact]
    public async Task Members_ScopeModeIsPresentForEveryone_ToWhoeverManagesScope()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);

        var (_, body) = await omar.GetAsync("/members?limit=200");

        var modes = body.GetProperty("items").EnumerateArray()
            .ToDictionary(m => m.GetProperty("username").GetString()!, m => m.GetProperty("scope_mode").GetString());
        Assert.Equal("all", modes["omar"]);
        Assert.Equal("assigned", modes["khaled"]);
        Assert.Equal("assigned", modes["rami"]);
    }

    // ---- GET /members/invitations — the pending invitations, so one can be revoked without knowing its id.

    /// <summary>
    /// The list carries what the screen shows and nothing more. **Never the token, and never its hash**: the token
    /// is the invitation's one secret, returned once to its creator (DEVELOPER_GUIDE §9) and never listed again.
    /// Asserted against the raw body, not a parsed field, so a token added under any name fails this.
    /// </summary>
    [Fact]
    public async Task Invitations_AreTheTenantsPending_AndCarryNoToken()
    {
        using var world = await World.BootstrapAsync("b01-invitations");
        var (invited, invitationId, token) = await world.InviteAsync(world.Owner, "listed@b01.test", "operator", "assigned");
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);

        var response = await world.Owner.Browser.Client.GetAsync("/members/invitations?limit=200");
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = JsonDocument.Parse(raw).RootElement.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("id").GetGuid() == invitationId);
        Assert.Equal("listed@b01.test", row.GetProperty("email").GetString());
        Assert.Equal("pending", row.GetProperty("status").GetString());
        Assert.Equal("assigned", row.GetProperty("intended_scope_mode").GetString());
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, raw, StringComparison.Ordinal);
    }

    /// <summary>
    /// Managing scope is not managing members: Rami assigns clients and invites nobody, so the invitation list —
    /// a list of addresses the organization has approached — is not his to read.
    /// </summary>
    [Fact]
    public async Task Invitations_AreRefusedToWhoeverManagesScopeAlone()
    {
        using var rami = await Browser.SignedInAsync("rami", Seed.AlAmin);

        var (status, body) = await rami.GetAsync("/members/invitations");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("not_permitted", body.GetProperty("error").GetString());
    }

    // ---- GET /scope/memberships/{id} — a colleague's mode and assignments. The second axis's own screen.

    /// <summary>
    /// Every assignment row, withdrawn ones included. §4.8: one permanent row per (membership, entity), disabled and
    /// reactivated, never deleted — so a screen that showed only the active ones would reactivate rows its user never
    /// saw the moment the mode went back to 'assigned'. The withdrawn row is data, not absence.
    /// </summary>
    [Fact]
    public async Task AMembersScope_IsItsModeAndEveryAssignment_WithdrawnOnesIncluded()
    {
        using var world = await World.BootstrapAsync("b01-scope-read");
        var kept = await world.ClientAsync("Kept");
        var withdrawn = await world.ClientAsync("Withdrawn");
        using var member = await world.JoinAsync("b01-assigned", "operator", "assigned");
        foreach (var client in new[] { kept, withdrawn })
            Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
                $"/scope/memberships/{member.MembershipId}/assignments/{client}", new { active = true, reason = "b01" })).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await world.Owner.Browser.PutAsync(
            $"/scope/memberships/{member.MembershipId}/assignments/{withdrawn}", new { active = false, reason = "b01 withdrawn" })).Status);

        var (status, body) = await world.Owner.Browser.GetAsync($"/scope/memberships/{member.MembershipId}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("assigned", body.GetProperty("scope_mode").GetString());
        var rows = body.GetProperty("assignments").EnumerateArray()
            .ToDictionary(a => a.GetProperty("scope_ref_id").GetGuid(), a => a.GetProperty("active").GetBoolean());
        Assert.True(rows[kept]);
        Assert.False(rows[withdrawn]);
    }

    /// <summary>A member manager who does not manage scope is refused: the surface is core.scope.manage's alone.</summary>
    [Fact]
    public async Task AMembersScope_IsRefusedToWhoeverDoesNotManageScope()
    {
        using var world = await World.BootstrapAsync("b01-scope-refused");
        var role = await world.CustomRoleAsync("core.members.manage");
        using var manager = await world.JoinAsync("b01-mm2", role, "all");

        var (status, body) = await manager.Browser.GetAsync($"/scope/memberships/{world.Owner.MembershipId}");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("not_permitted", body.GetProperty("error").GetString());
    }

    // ---- Nothing a request carries grants any of this (Test 17-c's shape, for the new surfaces).

    [Theory]
    [InlineData("/roles")]
    [InlineData("/members")]
    [InlineData("/members/invitations")]
    public async Task NothingInTheQueryOrAHeader_GrantsTheseReads(string path)
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);

        var (status, _) = await khaled.GetAsync(path, request =>
        {
            request.RequestUri = new Uri($"{path}?can_manage_members=true&can_manage_scope=true&scope_all=true", UriKind.Relative);
            request.Headers.Add("X-Can-Manage-Members", "true");
            request.Headers.Add("X-Can-Manage-Scope", "true");
        });

        Assert.Equal(HttpStatusCode.Forbidden, status);
    }

    private static async Task<List<string>> RoleCodesOfAsync(string tenant)
    {
        var codes = await Seed.ScalarStringAsMigratorAsync(
            """
            SELECT coalesce(string_agg(r.code COLLATE "C", ',' ORDER BY r.code COLLATE "C"), '')
            FROM roles r JOIN tenants t ON t.id = r.tenant_id WHERE t.name = @tenant
            """, ("tenant", tenant));
        return codes.Length == 0 ? [] : codes.Split(',').ToList();
    }
}

using System.Net;

namespace Conformance;

/// <summary>
/// The role catalogue carries each role's permissions (the project owner's item 36, group ج): the screens warn when
/// a role that manages members or scope is combined with scope <c>assigned</c>, and they read which roles do from
/// the server, by permission — never from a role's name, so every vertical's roles are covered by their data.
/// <para>
/// Who may read <c>GET /roles</c> is unchanged: <c>core.members.manage</c> or <c>core.scope.manage</c>. Nothing new is
/// disclosed: <c>role_permissions</c> and <c>permissions</c> were already readable by <c>app_user</c> in its own
/// tenant — measured on PostgreSQL 18.6 as Rami, who holds <c>core.scope.manage</c> alone, before this was built.
/// </para>
/// </summary>
public class RoleCatalogueTests
{
    /// <summary>Every role's permissions, exactly as the database holds them, sorted by code.</summary>
    [Theory]
    [InlineData("omar")] // owner: both management permissions
    [InlineData("rami")] // core.scope.manage alone — the scope screen's reader
    public async Task EachRole_CarriesItsPermissions_AsTheDatabaseHoldsThem(string username)
    {
        using var browser = await Browser.SignedInAsync(username, Seed.AlAmin);

        var (status, body) = await browser.GetAsync("/roles");

        Assert.Equal(HttpStatusCode.OK, status);
        foreach (var role in body.GetProperty("items").EnumerateArray())
        {
            var code = role.GetProperty("code").GetString()!;
            var permissions = role.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
            Assert.Equal(await PermissionsOfAsync(code), permissions);
        }
    }

    /// <summary>The two management permissions are what the warning reads; the seed's admin carries both.</summary>
    [Fact]
    public async Task TheAdminRole_CarriesBothManagementPermissions_AndViewerNeither()
    {
        using var rami = await Browser.SignedInAsync("rami", Seed.AlAmin);

        var (_, body) = await rami.GetAsync("/roles");

        var byCode = body.GetProperty("items").EnumerateArray().ToDictionary(
            r => r.GetProperty("code").GetString()!,
            r => r.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList());
        Assert.Contains("core.members.manage", byCode["admin"]);
        Assert.Contains("core.scope.manage", byCode["admin"]);
        Assert.DoesNotContain("core.members.manage", byCode["viewer"]);
        Assert.DoesNotContain("core.scope.manage", byCode["viewer"]);
    }

    private static async Task<List<string>> PermissionsOfAsync(string roleCode)
    {
        var codes = await Seed.ScalarStringAsMigratorAsync(
            """
            SELECT coalesce(string_agg(p.code COLLATE "C", ',' ORDER BY p.code COLLATE "C"), '')
              FROM roles r JOIN tenants t ON t.id = r.tenant_id
              JOIN role_permissions rp ON rp.role_id = r.id JOIN permissions p ON p.id = rp.permission_id
             WHERE t.name = @tenant AND r.code = @code
            """, ("tenant", Seed.AlAmin), ("code", roleCode));
        return codes.Length == 0 ? [] : [.. codes.Split(',')];
    }
}

using System.Diagnostics;
using System.Net;
using Npgsql;
using Xunit.Abstractions;

namespace Conformance;

/// <summary>
/// Tests that change the seed during a live session and restore it afterwards, or that measure timing: they run
/// alone, after the parallel tests, so no other test sees the seed mid-change or competes for the CPU.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SeedMutationCollection
{
    public const string Name = "seed mutations and timing";
}

// PROOF_SPEC v1.2 T3 — T3.3 (Test 20), T3.4 (Test 24-c), T3.5 (failed login), T3.8 (Test 18, second part).
[Collection(SeedMutationCollection.Name)]
public class T3_SessionTests(ITestOutputHelper output)
{
    private static readonly Guid ClientA = new("0199c000-0000-7000-8000-0000000000a1");
    private static readonly Guid ClientB = new("0199c000-0000-7000-8000-0000000000a2");

    // ---- T3.3 [B] — Test 20: a scope change propagates to the next request, with no re-login.
    // (The part "sees zero rows for that entity" needs the first scoped table — T5, OPEN_ITEMS.)

    [Fact]
    public async Task T3_3_Test20_LoweringModeAllToAssigned_TakesEffectOnTheNextRequest()
    {
        var layla = await Seed.MembershipAsync("layla", Seed.AlAmin);
        using var laylaBrowser = await Browser.SignedInAsync("layla", Seed.AlAmin);
        using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin);
        Assert.Equal("all", (await laylaBrowser.ScopeAsync()).Mode);

        try
        {
            var (status, body) = await sara.PutAsync($"/scope/memberships/{layla}/mode", new { scope_mode = "assigned" });
            Assert.True(status == HttpStatusCode.NoContent, body);

            // The same session, the same cookie: the next request resolves the new mode.
            Assert.Equal("assigned", (await laylaBrowser.ScopeAsync()).Mode);
        }
        finally
        {
            var (status, body) = await sara.PutAsync($"/scope/memberships/{layla}/mode", new { scope_mode = "all" });
            Assert.True(status == HttpStatusCode.NoContent, body);
        }
        Assert.Equal("all", (await laylaBrowser.ScopeAsync()).Mode);
    }

    [Fact]
    public async Task T3_3_Test20_RevokingAnAssignment_TakesEffectOnTheNextRequest()
    {
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        using var khaledBrowser = await Browser.SignedInAsync("khaled", Seed.AlAmin);
        using var sara = await Browser.SignedInAsync("sara", Seed.AlAmin);
        Assert.Equal([ClientA, ClientB], (await khaledBrowser.ScopeAsync()).Assignments);

        try
        {
            var (status, body) = await sara.PutAsync($"/scope/memberships/{khaled}/assignments/{ClientB}",
                new { active = false, reason = "Test 20" });
            Assert.True(status == HttpStatusCode.NoContent, body);

            Assert.Equal([ClientA], (await khaledBrowser.ScopeAsync()).Assignments);
        }
        finally
        {
            var (status, body) = await sara.PutAsync($"/scope/memberships/{khaled}/assignments/{ClientB}",
                new { active = true, reason = "seed contract" });
            Assert.True(status == HttpStatusCode.NoContent, body);
        }
        Assert.Equal([ClientA, ClientB], (await khaledBrowser.ScopeAsync()).Assignments);
    }

    // ---- T3.4 [B] — Test 24-c: revoking core.scope.manage during a session → the next request fails on a
    // management action, with no re-login. Rami holds the permission through the custom 'scope-manager' role;
    // the role is taken off his membership (as migrator) and put back afterwards.

    [Fact]
    public async Task T3_4_Test24c_RevokingScopeManage_TheNextManagementActionFails()
    {
        var rami = await Seed.MembershipAsync("rami", Seed.AlAmin);
        var khaled = await Seed.MembershipAsync("khaled", Seed.AlAmin);
        var scopeManager = await Seed.RoleAsync(Seed.AlAmin, "scope-manager");
        var alAmin = await Seed.TenantAsync(Seed.AlAmin);
        using var ramiBrowser = await Browser.SignedInAsync("rami", Seed.AlAmin);
        var manage = () => ramiBrowser.PutAsync($"/scope/memberships/{khaled}/assignments/{ClientA}",
            new { active = true, reason = "seed contract" });

        Assert.True((await ramiBrowser.ScopeAsync()).CanManage);
        Assert.Equal(HttpStatusCode.NoContent, (await manage()).Status);

        var link = await ScalarGuidAsync(
            "SELECT id FROM membership_roles WHERE membership_id = @m AND role_id = @r", ("m", rami), ("r", scopeManager));
        await ExecuteAsMigratorAsync("DELETE FROM membership_roles WHERE id = @id", ("id", link));
        try
        {
            var (status, body) = await manage();
            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.Contains("not_permitted", body);
            Assert.False((await ramiBrowser.ScopeAsync()).CanManage);
        }
        finally
        {
            await ExecuteAsMigratorAsync(
                "INSERT INTO membership_roles (id, tenant_id, membership_id, role_id) VALUES (@id, @t, @m, @r)",
                ("id", link), ("t", alAmin), ("m", rami), ("r", scopeManager));
        }
        Assert.True((await ramiBrowser.ScopeAsync()).CanManage);
    }

    // ---- T3.5 [B] — a failed login → a row in auth_attempts; and an existing versus a non-existent username →
    // identical responses, in text and in approximate timing.

    [Fact]
    public async Task T3_5_FailedLogin_WritesAnAttempt_AndResponsesAreIdentical()
    {
        var missing = "no-such-user-" + Guid.CreateVersion7().ToString("N");
        using var browser = new Browser();
        var before = await AttemptsAsync("khaled", succeeded: false);

        var wrong = await browser.LoginAsync("khaled", "not-the-password");
        var absent = await browser.LoginAsync(missing, "not-the-password");

        Assert.Equal(before + 1, await AttemptsAsync("khaled", succeeded: false));
        Assert.Equal(1, await AttemptsAsync(missing, succeeded: false));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, absent.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await absent.Content.ReadAsStringAsync());
        Assert.Equal(wrong.Content.Headers.ContentType?.ToString(), absent.Content.Headers.ContentType?.ToString());
        Assert.False(wrong.Headers.Contains("Set-Cookie"));
        Assert.False(absent.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task T3_5_FailedLogin_ExistingAndMissingUsername_TakeApproximatelyTheSameTime()
    {
        const int rounds = 12;
        using var browser = new Browser();
        // Warm-up: first requests pay for JIT and connection setup.
        await browser.LoginAsync("khaled", "warm-up");
        await browser.LoginAsync("no-such-user-warm-up", "warm-up");

        var existing = new List<double>();
        var absent = new List<double>();
        for (var i = 0; i < rounds; i++)
        {
            // Interleaved, so drift in the machine's load falls on both sides alike.
            existing.Add(await TimeAsync(() => browser.LoginAsync("khaled", "not-the-password-" + i)));
            absent.Add(await TimeAsync(() => browser.LoginAsync("no-such-user-" + Guid.CreateVersion7().ToString("N"), "not-the-password-" + i)));
        }

        var (e, a) = (Median(existing), Median(absent));
        var ratio = Math.Max(e, a) / Math.Min(e, a);
        output.WriteLine($"T3.5 timing: median existing {e:F1} ms, missing {a:F1} ms, ratio {ratio:F2} ({rounds} rounds each)");
        Assert.True(ratio < 1.5, $"median existing {e:F1} ms vs missing {a:F1} ms (ratio {ratio:F2})");
    }

    // ---- T3.8 [B] — Test 18, second part: a membership with no membership_scope row → a loud error at tenant
    // selection, not a silent default; and zero rows beneath it if that is bypassed.

    [Fact]
    public async Task T3_8_Test18_MembershipWithoutScopeRow_LoudAtTenantSelection()
    {
        var layla = await Seed.UserAsync("layla");
        var maan = await Seed.TenantAsync(Seed.Maan);
        var membership = Guid.CreateVersion7();
        await ExecuteAsMigratorAsync(
            "INSERT INTO memberships (id, tenant_id, user_id, status, created_at) VALUES (@m, @t, @u, 'active', now())",
            ("m", membership), ("t", maan), ("u", layla));
        try
        {
            using var browser = await Browser.SignedInAsync("layla");

            var response = await browser.SelectAsync(maan);

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("membership_scope_missing", (await browser.JsonAsync(response)).GetProperty("error").GetString());
            // Not a silent default: the tenant was not entered.
            var (status, _) = await browser.GetAsync("/me/scope");
            Assert.Equal(HttpStatusCode.Conflict, status);

            // Beneath it, if bypassed: the membership set, no scope variable resolved → the second axis shows nothing.
            await using var session = await Session.OpenAsync(Target.AppUser, user: layla, tenant: maan, membership: membership);
            await session.ExecuteAsync(
                "INSERT INTO audit_log (id, tenant_id, actor_type, action, entity_type, created_at) VALUES (@id, @t, 'user', 't3.8', 'x', now())",
                ("id", Guid.CreateVersion7()), ("t", maan));
            Assert.Equal(0, await session.CountAsync("SELECT count(*) FROM audit_log"));
        }
        finally
        {
            await ExecuteAsMigratorAsync("DELETE FROM memberships WHERE id = @m", ("m", membership));
        }
    }

    private static async Task<double> TimeAsync(Func<Task<HttpResponseMessage>> request)
    {
        var watch = Stopwatch.StartNew();
        using var response = await request();
        watch.Stop();
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        return watch.Elapsed.TotalMilliseconds;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    private static Task<long> AttemptsAsync(string username, bool succeeded) =>
        Seed.CountAsMigratorAsync("SELECT count(*) FROM auth_attempts WHERE username_entered = @u AND succeeded = @s",
            ("u", username), ("s", succeeded));

    private static async Task<Guid> ScalarGuidAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (Guid)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No row: {sql}"));
    }

    private static async Task ExecuteAsMigratorAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = await Target.OpenAsync(Target.Migrator);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }
}

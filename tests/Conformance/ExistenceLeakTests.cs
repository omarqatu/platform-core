using System.Net;

namespace Conformance;

/// <summary>
/// A row the caller may not see, and a row that does not exist, answer <b>alike</b> — same status, same body, byte
/// for byte (the project owner's decision, group ب, after the premise was measured).
/// <para>
/// The question raised was whether <c>403 not_permitted</c> on a hidden client confirms that the client exists, and
/// whether 404 should replace it. It does not confirm it. Every one of these endpoints branches on one thing —
/// <c>row is null</c> — and RLS gives null for a hidden row exactly as the database gives null for an id that was
/// never written. Measured on PostgreSQL 18.6 before this test was written: as an assigned member, a <c>SELECT</c>
/// for a client of the same tenant that is not assigned and a <c>SELECT</c> for a uuid that exists nowhere both
/// return zero rows.
/// </para>
/// <para>
/// So 404 would have bought nothing, at the cost of a new error code and a declared exception on the tests that
/// pin 403 (<c>T5_ClientLifecycleTests</c>: "invisible to read and to write, and the two answer alike"). The
/// answer stays 403, and what was an argument becomes this file: the indistinguishability is now a contract with
/// a test on it, instead of a property nobody was checking.
/// </para>
/// </summary>
public class ExistenceLeakTests
{
    /// <summary>A uuid of the right shape that was never written to any table.</summary>
    private static readonly Guid Nowhere = new("0199ffff-ffff-7fff-8fff-ffffffffff01");

    /// <summary>
    /// Khaled is assigned A and B. C is his tenant's client and is not his: it exists, he may not see it. The
    /// answer must be the one an unknown uuid gets.
    /// </summary>
    [Fact]
    public async Task AClientThatIsHidden_AndAUuidThatIsNowhere_AnswerAlike()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);
        var hidden = await Seed.ClientAsync(Seed.AlAmin, "C");

        var (hiddenStatus, hiddenBody) = await RawAsync(khaled, $"/subscriptions/clients/{hidden}");
        var (unknownStatus, unknownBody) = await RawAsync(khaled, $"/subscriptions/clients/{Nowhere}");

        Assert.Equal(HttpStatusCode.Forbidden, hiddenStatus);
        Assert.Equal(unknownStatus, hiddenStatus);
        Assert.Equal(unknownBody, hiddenBody);
    }

    /// <summary>The same for a subscription: its own row is hidden by the same restrictive policy on its client.</summary>
    [Fact]
    public async Task ASubscriptionThatIsHidden_AndAUuidThatIsNowhere_AnswerAlike()
    {
        using var khaled = await Browser.SignedInAsync("khaled", Seed.AlAmin);
        var hidden = await HiddenSubscriptionAsync();

        var (hiddenStatus, hiddenBody) = await RawAsync(khaled, $"/subscriptions/{hidden}");
        var (unknownStatus, unknownBody) = await RawAsync(khaled, $"/subscriptions/{Nowhere}");

        Assert.Equal(HttpStatusCode.Forbidden, hiddenStatus);
        Assert.Equal(unknownStatus, hiddenStatus);
        Assert.Equal(unknownBody, hiddenBody);
    }

    /// <summary>
    /// Another tenant's client, to the owner of this one: the first axis answers as the second does, so neither
    /// says which of the two hid the row.
    /// </summary>
    [Fact]
    public async Task AnotherTenantsClient_AndAUuidThatIsNowhere_AnswerAlike()
    {
        using var omar = await Browser.SignedInAsync("omar", Seed.AlAmin);
        var foreign = await Seed.ClientAsync(Seed.Maan, "X");

        var (foreignStatus, foreignBody) = await RawAsync(omar, $"/subscriptions/clients/{foreign}");
        var (unknownStatus, unknownBody) = await RawAsync(omar, $"/subscriptions/clients/{Nowhere}");

        Assert.Equal(HttpStatusCode.Forbidden, foreignStatus);
        Assert.Equal(unknownStatus, foreignStatus);
        Assert.Equal(unknownBody, foreignBody);
    }

    /// <summary>The body as it is sent, not as a parser rebuilds it: the claim is that the bytes match.</summary>
    private static async Task<(HttpStatusCode Status, string Body)> RawAsync(Browser browser, string path)
    {
        var response = await browser.Client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>A subscription of client C — Khaled's tenant, not Khaled's client. Read as migrator, as the
    /// harness reads every ground truth.</summary>
    private static async Task<Guid> HiddenSubscriptionAsync()
    {
        var id = await Seed.ScalarStringAsMigratorAsync(
            """
            SELECT s.id::text FROM subscriptions s JOIN clients c ON c.id = s.scope_ref_id
            JOIN tenants t ON t.id = s.tenant_id
            WHERE t.name = @tenant AND c.name = 'C' ORDER BY s.id LIMIT 1
            """, ("tenant", Seed.AlAmin));
        return Guid.Parse(id);
    }
}

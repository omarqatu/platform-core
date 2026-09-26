using System.Net;
using System.Text.RegularExpressions;

namespace Conformance;

// PROOF_SPEC v1.3 T8.2 — the one RTL screen: the subscription list, right-to-left, displays the scope declaration in
// human phrasing, and shows no total figure at all to an assigned member. The screen is GET /subscriptions/screen,
// rendered on the server, read here as HTML on the seed contract.
public partial class T8_ScreenTests
{
    [Fact]
    public async Task T8_2_Assigned_SeesTheDeclaration_AndNoTotalFigureAtAll()
    {
        var html = await ScreenAsync("khaled");

        Assert.Matches("<html[^>]*\\blang=\"ar\"[^>]*\\bdir=\"rtl\"", html);
        var scope = Declaration(html);
        Assert.Contains("data-scope-mode=\"assigned\"", scope.Tag);
        Assert.Contains("المُسندين إليك", scope.Text);
        Assert.Contains("عميلان", scope.Text);           // two assigned clients
        Assert.Contains("10 اشتراكات", scope.Text);      // their ten subscriptions
        // No total, anywhere: no data-total, and the tenant's total (20 subscriptions, 4 clients) in no text of the page.
        Assert.DoesNotContain("data-total", html);
        var text = Text(html);
        Assert.DoesNotContain("20 اشتراك", text);
        Assert.DoesNotContain("4 عملاء", text);
        Assert.DoesNotContain("المستأجر", scope.Text);
        Assert.Equal(10, Rows(html));
    }

    [Fact]
    public async Task T8_2_OneAssignedClient_TheDeclarationSaysSo()
    {
        var scope = Declaration(await ScreenAsync("rami"));

        Assert.Contains("data-scope-mode=\"assigned\"", scope.Tag);
        Assert.Contains("عميل واحد", scope.Text);
        Assert.Contains("5 اشتراكات", scope.Text);
    }

    [Fact]
    public async Task T8_2_All_SeesTheDeclaration_WithTheTotal()
    {
        var html = await ScreenAsync("sara");

        var scope = Declaration(html);
        Assert.Contains("data-scope-mode=\"all\"", scope.Tag);
        Assert.Contains("data-total=\"20\"", scope.Tag);
        Assert.Contains("20 اشتراكاً", scope.Text);
        Assert.Equal(20, Rows(html));
    }

    // The bidirectional layout: right-to-left chrome; left-to-right content isolated, dates in explicit LTR.
    [Fact]
    public async Task T8_2_LeftToRightContent_IsIsolated()
    {
        var html = await ScreenAsync("khaled");

        Assert.Matches("<td><bdi>A service 1</bdi></td>", html);
        Assert.Matches("<bdi dir=\"ltr\"><time datetime=\"2027-01-02\">2027-01-02</time></bdi>", html);
    }

    private static async Task<string> ScreenAsync(string username)
    {
        using var browser = await Browser.SignedInAsync(username, Seed.AlAmin);
        var response = await browser.Client.GetAsync("/subscriptions/screen");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    private static (string Tag, string Text) Declaration(string html)
    {
        var match = DeclarationPattern().Match(html);
        Assert.True(match.Success, "The screen carries no scope declaration.");
        return (match.Groups["tag"].Value, match.Groups["text"].Value);
    }

    private static int Rows(string html) => RowPattern().Count(html[html.IndexOf("<tbody>", StringComparison.Ordinal)..]);

    private static string Text(string html) => TagPattern().Replace(html, " ");

    [GeneratedRegex("(?<tag><p class=\"scope\"[^>]*>)(?<text>.*?)</p>")]
    private static partial Regex DeclarationPattern();

    [GeneratedRegex("<tr>")]
    private static partial Regex RowPattern();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();
}

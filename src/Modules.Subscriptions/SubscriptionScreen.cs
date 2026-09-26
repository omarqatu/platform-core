using System.Globalization;
using System.Net;
using System.Text;
using Core.Data;
using Core.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Modules.Subscriptions;

/// <summary>
/// The one RTL screen (PROOF_SPEC T8): the subscription list, right-to-left, with the scope declaration (3.5) in human
/// phrasing — "showing the subscriptions of the clients assigned to you". Rendered on the server from the same data and
/// the same declaration as GET /subscriptions, so what it shows is testable without a browser. An assigned member sees
/// no total figure at all: the tenant's total is neither in the text nor in the markup (Test 19). No design system —
/// one component proving the bidirectional layout: Arabic chrome, left-to-right content isolated in &lt;bdi&gt;.
/// </summary>
public static class SubscriptionScreen
{
    public const int PageSize = 50;

    public static void MapSubscriptionScreen(this IEndpointRouteBuilder app)
    {
        app.MapGet("/subscriptions/screen", async (SubscriptionsDbContext db, ISessionContextAccessor session, CancellationToken ct) =>
        {
            if (session.Current.TenantId is null)
                return Results.Json(new { error = "no_active_tenant" }, statusCode: StatusCodes.Status409Conflict);
            var html = await UnitOfWork.RunAsync(db, session.Current, async (c, t) =>
            {
                var scope = await Permissions.RequireAsync(c, SubscriptionEndpoints.Read, t);
                var subscriptions = await ScopedList.PageAsync(scope,
                    c.Subscriptions.OrderBy(s => s.EndsOn).ThenBy(s => s.Id)
                        .Join(c.Clients, s => s.ScopeRefId, x => x.Id, (s, x) => new Row(s.ServiceName, x.Name, s.EndsOn)),
                    0, PageSize, t);
                var clients = await c.Clients.LongCountAsync(t);
                return Render(subscriptions, clients);
            }, ct);
            return Results.Content(html, "text/html; charset=utf-8");
        }).RequireAuthorization().WithMetadata(new OwnUnitsOfWorkAttribute());
    }

    public sealed record Row(string Service, string Client, DateOnly EndsOn);

    private static string Render(ScopedList<Row> page, long clients)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html>
            <html lang="ar" dir="rtl">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>الاشتراكات</title>
            <style>
              body { font-family: system-ui, "Noto Sans Arabic", sans-serif; margin: 0; padding: 1.5rem; }
              main { max-inline-size: 48rem; margin-inline: auto; }
              .scope { padding-block: .5rem; padding-inline-start: .75rem; border-inline-start: 4px solid #2f6f4e; background: #eef6f1; }
              table { inline-size: 100%; border-collapse: collapse; margin-block-start: 1rem; }
              th, td { text-align: start; padding: .4rem .6rem; border-block-end: 1px solid #ddd; }
              .date { font-variant-numeric: tabular-nums; }
            </style>
            </head>
            <body>
            <main>
            <h1>الاشتراكات</h1>

            """);
        // The declaration. Under 'all' the total is the visible count (3.5); under 'assigned' it is not rendered at all.
        if (page.ScopeMode == "all")
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"scope\" data-scope-mode=\"all\" data-total=\"{page.TotalCount}\">تُعرض لك كل اشتراكات المستأجر: {Count(page.VisibleCount, Subscription)}.</p>\n");
        else if (page.VisibleCount == 0)
            html.Append("<p class=\"scope\" data-scope-mode=\"assigned\">تُعرض لك اشتراكات العملاء المُسندين إليك فقط، ولا اشتراكات لديهم الآن.</p>\n");
        else
            html.Append(CultureInfo.InvariantCulture,
                $"<p class=\"scope\" data-scope-mode=\"assigned\">تُعرض لك اشتراكات العملاء المُسندين إليك فقط: {Count(clients, Client)}، {Count(page.VisibleCount, Subscription)}.</p>\n");

        html.Append("<table>\n<thead><tr><th scope=\"col\">الخدمة</th><th scope=\"col\">العميل</th><th scope=\"col\">ينتهي في</th></tr></thead>\n<tbody>\n");
        foreach (var row in page.Items)
        {
            var date = row.EndsOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            html.Append(CultureInfo.InvariantCulture,
                $"<tr><td><bdi>{WebUtility.HtmlEncode(row.Service)}</bdi></td><td><bdi>{WebUtility.HtmlEncode(row.Client)}</bdi></td>" +
                $"<td class=\"date\"><bdi dir=\"ltr\"><time datetime=\"{date}\">{date}</time></bdi></td></tr>\n");
        }
        html.Append("</tbody>\n</table>\n");
        if (page.HasMoreInScope)
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"more\">تُعرض أول {Count(page.Items.Count, Subscription)}، وضمن نطاقك المزيد.</p>\n");
        html.Append("</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static readonly (string One, string Two, string Few, string Many) Client = ("عميل واحد", "عميلان", "عملاء", "عميلاً");
    private static readonly (string One, string Two, string Few, string Many) Subscription = ("اشتراك واحد", "اشتراكان", "اشتراكات", "اشتراكاً");

    /// <summary>A counted noun in Arabic: one, two, 3–10 (plural), 11 and above (singular accusative).</summary>
    public static string Count(long n, (string One, string Two, string Few, string Many) noun) => n switch
    {
        1 => noun.One,
        2 => noun.Two,
        >= 3 and <= 10 => $"{n} {noun.Few}",
        _ when n % 100 is >= 3 and <= 10 => $"{n} {noun.Few}",
        _ => $"{n} {noun.Many}",
    };
}

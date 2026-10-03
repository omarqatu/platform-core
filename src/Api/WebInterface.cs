using Microsoft.Extensions.FileProviders;

namespace Api;

/// <summary>
/// The web interface (web/), served by Api from the same origin as the API: no CORS, and the session stays the
/// HTTP-only cookie. The path space is split (OPEN_ITEMS 37, decided by the project owner):
/// <list type="bullet">
/// <item>the API under <see cref="ApiPrefix"/>, and only there — every route is registered in that group (Program);</item>
/// <item>the interface everywhere else (its screens under /app/, plus /login and /organizations);</item>
/// <item>one documented exception, <see cref="ServerRenderedScreens"/>.</item>
/// </list>
/// After routing, a request no route matched is answered here: a GET for a page (Accept: text/html) outside /api and
/// outside the server-rendered screens is index.html — the interface routes on the client — or a file of the build;
/// anything else is a plain 404 (no body), never index.html. The built interface (web/dist) lives under Web:Root, by
/// default wwwroot beside Api.dll (the image copies it there); without it, every unmatched request is a 404.
/// </summary>
public static class WebInterface
{
    public const string ApiPrefix = "/api";

    /// <summary>
    /// The routes outside /api and the interface: the original T8 screen alone (Modules.Subscriptions.SubscriptionScreen),
    /// part of a merged proof (PROOF_SPEC T8.2) and never edited, so it keeps its path. The interface never answers
    /// under it.
    /// </summary>
    public static readonly string[] ServerRenderedScreens = ["/subscriptions/screen"];

    /// <summary>After UseRouting.</summary>
    public static void UseWebInterface(this WebApplication app)
    {
        var root = app.Configuration["Web:Root"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var files = File.Exists(Path.Combine(root, "index.html")) ? new PhysicalFileProvider(Path.GetFullPath(root)) : null;

        if (files is not null)
            app.UseWhen(InterfaceGet, branch => branch.UseStaticFiles(new StaticFileOptions { FileProvider = files }));
        app.Use(async (http, next) =>
        {
            if (http.GetEndpoint() is not null)
            {
                await next(http);
                return;
            }
            if (files is not null && InterfaceGet(http) && Path.GetExtension(http.Request.Path.Value) is "" or null &&
                http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                http.Response.ContentType = "text/html; charset=utf-8";
                http.Response.Headers.CacheControl = "no-cache";
                await http.Response.SendFileAsync(files.GetFileInfo("index.html"), http.RequestAborted);
                return;
            }
            // No route, and not the interface's: a real 404, before any unit of work.
            http.Response.StatusCode = StatusCodes.Status404NotFound;
        });
    }

    /// <summary>A GET no route matched, outside /api and outside the server-rendered screens.</summary>
    private static bool InterfaceGet(HttpContext http) =>
        HttpMethods.IsGet(http.Request.Method) && http.GetEndpoint() is null &&
        !http.Request.Path.StartsWithSegments(ApiPrefix, StringComparison.OrdinalIgnoreCase) &&
        !ServerRenderedScreens.Any(s => http.Request.Path.StartsWithSegments(s, StringComparison.OrdinalIgnoreCase));
}

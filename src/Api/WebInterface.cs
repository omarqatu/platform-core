using Microsoft.Extensions.FileProviders;

namespace Api;

/// <summary>
/// The web interface (web/), served by Api from the same origin as the API: no CORS, and the session stays the
/// HTTP-only cookie. The interface calls the API under <see cref="ApiPrefix"/>; that prefix is removed before routing,
/// so every route keeps its path and behaviour — the same request without the prefix is answered the same way. The
/// built interface (web/dist) lives under Web:Root, by default wwwroot beside Api.dll (the image copies it there).
/// Where it is absent — a build without it, the in-process tests — nothing here does anything.
/// </summary>
public static class WebInterface
{
    public const string ApiPrefix = "/api";

    /// <summary>Before UseRouting: /api/x is routed as /x.</summary>
    public static void UseApiPrefix(this WebApplication app) => app.UsePathBase(ApiPrefix);

    /// <summary>
    /// After UseRouting. Only what no route matched: a GET for a file of the build is that file; a GET for a page
    /// (Accept: text/html, outside /api) is index.html — the interface routes on the client. Anything else is left to
    /// the pipeline exactly as before (a 404).
    /// </summary>
    public static void UseWebInterface(this WebApplication app)
    {
        var root = app.Configuration["Web:Root"] ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (!File.Exists(Path.Combine(root, "index.html")))
            return;
        var files = new PhysicalFileProvider(Path.GetFullPath(root));

        app.UseWhen(Unmatched, branch => branch.UseStaticFiles(new StaticFileOptions { FileProvider = files }));
        app.Use(async (http, next) =>
        {
            if (Unmatched(http) && Path.GetExtension(http.Request.Path.Value) is "" or null &&
                http.Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                http.Response.ContentType = "text/html; charset=utf-8";
                http.Response.Headers.CacheControl = "no-cache";
                await http.Response.SendFileAsync(files.GetFileInfo("index.html"), http.RequestAborted);
                return;
            }
            await next(http);
        });
    }

    private static bool Unmatched(HttpContext http) =>
        HttpMethods.IsGet(http.Request.Method) && !http.Request.PathBase.HasValue && http.GetEndpoint() is null;
}

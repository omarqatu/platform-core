using Core;

namespace Api;

/// <summary>
/// Cross-site request forgery (OPEN_ITEMS 28): the session cookie is SameSite=Strict, which a sibling subdomain — the
/// same site — passes. So every request with a method that is not safe (anything but GET, HEAD, OPTIONS, TRACE: POST,
/// PUT, PATCH, DELETE) needs both, or it is refused with 403 csrf_rejected:
/// <list type="bullet">
/// <item>an Origin header equal, character for character, to one of Security:AllowedOrigins. No Origin is a refusal,
/// never a fallback to Referer;</item>
/// <item>X-Requested-With: platform-web. A custom header makes any other origin's request a CORS preflight, which Api
/// never answers with permission (it configures no CORS); it also covers the endpoints that take no body.</item>
/// </list>
/// No exception for the paths without a session — login and invitation acceptance included (login CSRF). It runs
/// before routing, authentication and any unit of work, so a refused request reaches no database command.
/// </summary>
public static class CsrfProtection
{
    public const string AllowedOriginsKey = "Security:AllowedOrigins";
    public const string RequestedWithHeader = "X-Requested-With";
    public const string RequestedWithValue = "platform-web";

    /// <summary>
    /// The configured origins, checked at startup: at least one, each a serialized origin (scheme://host[:port], lower
    /// case, no default port, no path) — the only form a browser sends, so any other entry could never match.
    /// </summary>
    public static string[] AllowedOrigins(IConfiguration configuration)
    {
        var origins = configuration.GetSection(AllowedOriginsKey).Get<string[]>()?
            .Where(o => !string.IsNullOrEmpty(o)).ToArray() ?? [];
        if (origins.Length == 0)
            throw new InvalidOperationException($"{AllowedOriginsKey} is not configured: Api would refuse every unsafe request.");
        foreach (var origin in origins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
                !string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{AllowedOriginsKey} holds '{origin}', which is not an origin as a browser sends it (scheme://host[:port]).");
        }
        return origins;
    }

    /// <summary>Before routing, on every path — the API's, the interface's and the T8 screen's alike.</summary>
    public static void UseCsrfProtection(this WebApplication app, IReadOnlyCollection<string> allowedOrigins)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(CsrfProtection));
        app.Use(async (http, next) =>
        {
            if (IsSafe(http.Request.Method) || Accepted(http.Request, allowedOrigins))
            {
                await next(http);
                return;
            }
            logger.LogWarning("CSRF: {Method} {Path} refused (Origin {Origin}, X-Requested-With {RequestedWith}).",
                http.Request.Method, http.Request.Path, http.Request.Headers.Origin.Count > 0 ? "present" : "absent",
                http.Request.Headers[RequestedWithHeader].Count > 0 ? "present" : "absent");
            await Results.Json(new { error = ApiErrorCodes.CsrfRejected }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(http);
        });
    }

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method);

    private static bool Accepted(HttpRequest request, IReadOnlyCollection<string> allowedOrigins) =>
        request.Headers.Origin is { Count: 1 } origin && allowedOrigins.Contains(origin.ToString(), StringComparer.Ordinal) &&
        request.Headers[RequestedWithHeader] is { Count: 1 } requestedWith &&
        string.Equals(requestedWith.ToString(), RequestedWithValue, StringComparison.Ordinal);
}

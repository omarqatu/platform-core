using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Api;

/// <summary>
/// The client's address behind a reverse proxy (OPEN_ITEMS 85). Api reads the caller's address from the connection
/// — the attempt limiter keys on it (Amendment 01 §5.2: the per-ip ceiling, the per-username-and-ip delay), and
/// auth_attempts and audit_log record it. Behind nginx every connection comes from nginx, so without this every
/// client would share one address: one ceiling for everyone, which any 50 failures in the window turn into a
/// lockout of every sign-in.
/// <para>
/// So X-Forwarded-For is believed from the proxies named in Network:TrustedProxies, and from nobody else: a request
/// whose connection is not one of them keeps its connection's address, whatever header it carries. One hop only —
/// the proxy's own entry, the last one; the proxy is configured to replace the header rather than append to it
/// (deploy/nginx). With none configured (development, CI) nothing is read from any header.
/// </para>
/// Exact addresses only, never a network: a network would trust every address in it.
/// </summary>
public static class ReverseProxy
{
    public const string TrustedProxiesKey = "Network:TrustedProxies";

    /// <summary>The framework's own switch, as ASPNETCORE_FORWARDEDHEADERS_ENABLED lands in configuration.</summary>
    private const string FrameworkSwitchKey = "FORWARDEDHEADERS_ENABLED";

    /// <summary>
    /// The configured proxies, checked at startup: each one IP address, written as it parses. And the framework's own
    /// switch (ASPNETCORE_FORWARDEDHEADERS_ENABLED) refused: it installs the same middleware with both trust lists
    /// empty, which trusts every peer — any client could then choose its address.
    /// </summary>
    public static IPAddress[] TrustedProxies(IConfiguration configuration)
    {
        if (string.Equals(configuration[FrameworkSwitchKey], "true", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"ASPNETCORE_{FrameworkSwitchKey}=true trusts X-Forwarded-For from every peer; name the proxies in {TrustedProxiesKey} instead.");

        var entries = configuration.GetSection(TrustedProxiesKey).Get<string[]>()?
            .Where(e => !string.IsNullOrEmpty(e)).ToArray() ?? [];
        return entries.Select(entry =>
            IPAddress.TryParse(entry, out var address) && string.Equals(address.ToString(), entry, StringComparison.Ordinal)
                ? address
                : throw new InvalidOperationException(
                    $"{TrustedProxiesKey} holds '{entry}', which is not one IP address (a network or a host name is refused).")).ToArray();
    }

    /// <summary>First in the pipeline: everything after it — CSRF, the limiter, the audit — sees the client's address.</summary>
    public static void UseClientAddress(this WebApplication app, IReadOnlyCollection<IPAddress> trustedProxies)
    {
        if (trustedProxies.Count == 0)
            return;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1,
        };
        // The defaults trust loopback; only what is configured is trusted.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var proxy in trustedProxies)
            options.KnownProxies.Add(proxy);
        app.UseForwardedHeaders(options);
    }
}

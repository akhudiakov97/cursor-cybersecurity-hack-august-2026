using HoneyGuard.Configuration;
using HoneyGuard.Reporting;
using Microsoft.Extensions.Options;

namespace HoneyGuard.Security;

/// <summary>
/// ASP.NET Core middleware that inspects every incoming request before it reaches your
/// real endpoints, and decides one of three things: block it, trap it, or let it through.
///
/// Middleware in ASP.NET Core is just a chain of objects, each one wrapping the next,
/// forming a "pipeline" that a request flows through and a response flows back out
/// through. This class follows the conventional shape for a middleware component: a
/// constructor that receives the <see cref="RequestDelegate"/> for "the rest of the
/// pipeline" plus anything it needs from DI, and an <c>InvokeAsync</c> method that runs
/// once per request. ASP.NET Core discovers this shape by convention (there is no
/// interface to implement) - see <see cref="HoneyGuardServiceCollectionExtensions.UseHoneyGuard"/>
/// for how it is added to the pipeline.
///
/// Per-request dependencies (like <see cref="BanRegistry"/> and <see cref="IncidentQueue"/>,
/// both singletons) are injected as constructor parameters here rather than through the
/// method, which is standard for middleware since ASP.NET Core creates one middleware
/// instance for the whole application and reuses it for every request.
/// </summary>
public sealed class HoneyGuardMiddleware(
    RequestDelegate next,
    BanRegistry banRegistry,
    IncidentQueue incidentQueue,
    IOptions<HoneyGuardOptions> options,
    ILogger<HoneyGuardMiddleware> logger)
{
    /// <summary>
    /// The framework calls this method for every request that reaches this point in the
    /// pipeline. Returning without calling <c>next(context)</c> short-circuits the
    /// pipeline - none of the endpoints further down (like /api/v1/products) ever run.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        HoneyGuardOptions settings = options.Value;
        string requestPath = context.Request.Path.Value ?? string.Empty;

        // Exempt paths (the dashboard itself, static assets, the config endpoint) always
        // pass straight through. Without this, banning your own machine's IP while
        // testing traps from the same box would also lock you out of the dashboard that
        // is supposed to be showing you the results.
        if (IsExemptPath(requestPath, settings.ExemptPathPrefixes))
        {
            await next(context);
            return;
        }

        string ipAddress = ResolveClientIpAddress(context, settings.TrustForwardedForHeader);

        // Branch 1: this caller is already banned from a previous trap hit. Block it
        // immediately with 403 Forbidden before any real endpoint logic runs.
        if (banRegistry.IsBanned(ipAddress))
        {
            logger.LogInformation("Blocked request from banned IP {IpAddress} to {Path}", ipAddress, requestPath);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        // Branch 2: this request is probing one of our fake vulnerable-looking routes.
        // Ban the IP and respond exactly like a route that does not exist, so the
        // scanner has no way to tell it just triggered a trap instead of a dead end.
        string? matchedTrap = FindMatchingTrap(requestPath, settings.TrapPaths);
        if (matchedTrap is not null)
        {
            banRegistry.Ban(ipAddress, matchedTrap, settings.BanDuration);

            // Reporting the incident is a fire-and-forget enqueue (see IncidentQueue),
            // so this line returns instantly and does not add any latency here.
            incidentQueue.TryReport(new Incident(
                IpAddress: ipAddress,
                Path: requestPath,
                Method: context.Request.Method,
                UserAgent: context.Request.Headers.UserAgent.ToString(),
                TrapName: matchedTrap,
                OccurredAtUtc: DateTimeOffset.UtcNow));

            logger.LogWarning("Trap {TrapName} hit by {IpAddress} - IP banned", matchedTrap, ipAddress);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Not Found");
            return;
        }

        // Branch 3: nothing suspicious - let the request continue to the real endpoints.
        await next(context);
    }

    /// <summary>
    /// True if the request path starts with one of the configured exempt prefixes.
    /// A simple loop reads more clearly here than a LINQ <c>Any(...)</c> call, and this
    /// runs on every single request, so keeping it obvious matters more than being terse.
    /// </summary>
    private static bool IsExemptPath(string requestPath, string[] exemptPathPrefixes)
    {
        // The root path "/" is only ever an exact match for the dashboard's own index
        // page - it must NOT be treated as a prefix, since every possible path (including
        // every honeypot trap and every real API route) technically "starts with" "/".
        // Every other configured entry is a genuine folder-style prefix, like
        // "/api/dashboard" covering "/api/dashboard/config" and "/api/dashboard/reset".
        foreach (string prefix in exemptPathPrefixes)
        {
            bool isMatch = prefix == "/"
                ? requestPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                : requestPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                  requestPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);

            if (isMatch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the configured trap path that matches the request, or null if none match.
    /// Comparisons are case-insensitive because real attackers (and scanners) frequently
    /// vary casing to try to slip past naive string checks.
    /// </summary>
    private static string? FindMatchingTrap(string requestPath, string[] trapPaths)
    {
        foreach (string trapPath in trapPaths)
        {
            if (requestPath.Equals(trapPath, StringComparison.OrdinalIgnoreCase))
            {
                return trapPath;
            }
        }

        return null;
    }

    /// <summary>
    /// Works out "who is making this request" as an IP address string.
    ///
    /// In production, the real caller's IP is <c>context.Connection.RemoteIpAddress</c>.
    /// But for a local demo, every request - the "attacker" curl commands and the
    /// dashboard's own polling - comes from the same loopback address, which makes for a
    /// boring demo and makes it impossible to ban one without banning the other. When
    /// <paramref name="trustForwardedForHeader"/> is enabled (Development only - see
    /// appsettings.Development.json), we honor an "X-Forwarded-For" header instead, so a
    /// request like <c>curl -H "X-Forwarded-For: 203.0.113.45" ...</c> shows up as that
    /// spoofed public IP everywhere in HoneyGuard.
    ///
    /// This must stay OFF in real production deployments unless a trusted reverse proxy
    /// guarantees it always sets that header itself - otherwise an attacker could simply
    /// claim to be a different IP than they really are to dodge the ban.
    /// </summary>
    private static string ResolveClientIpAddress(HttpContext context, bool trustForwardedForHeader)
    {
        if (trustForwardedForHeader && context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            string? spoofedIpAddress = forwardedFor.ToString();
            if (!string.IsNullOrWhiteSpace(spoofedIpAddress))
            {
                return spoofedIpAddress;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

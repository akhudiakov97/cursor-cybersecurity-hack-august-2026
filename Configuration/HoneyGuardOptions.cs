namespace HoneyGuard.Configuration;

/// <summary>
/// Strongly-typed settings for HoneyGuard, bound from the "HoneyGuard" section of
/// appsettings.json (and appsettings.Development.json, and user-secrets).
///
/// This is the standard .NET "Options pattern": instead of reading raw strings out of
/// <c>IConfiguration</c> all over the codebase, you describe your settings as a plain
/// class and let the framework bind JSON config into it once. Anything that needs the
/// settings then asks for <c>IOptions&lt;HoneyGuardOptions&gt;</c> (or the snapshot/monitor
/// variants) through dependency injection instead of re-parsing configuration itself.
/// See Program.cs for where this class is registered with
/// <c>builder.Services.Configure&lt;HoneyGuardOptions&gt;(...)</c>.
/// </summary>
public sealed class HoneyGuardOptions
{
    /// <summary>
    /// The config section name this type binds to, so Program.cs doesn't need to
    /// repeat the magic string "HoneyGuard" in more than one place.
    /// </summary>
    public const string SectionName = "HoneyGuard";

    /// <summary>
    /// Base URL of the Supabase project, e.g. "https://xxxx.supabase.co".
    /// Safe to keep in appsettings.json - it is not a secret.
    /// </summary>
    public string SupabaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// The publishable "anon" API key. This key is designed to be public - it is sent
    /// to the browser dashboard too - and it only grants whatever Row Level Security
    /// policies allow (in our case, read-only access to the incidents table).
    /// </summary>
    public string SupabaseAnonKey { get; set; } = string.Empty;

    /// <summary>
    /// The "service_role" key, which bypasses Row Level Security entirely. This is what
    /// lets the .NET backend insert incident rows even though anonymous clients cannot.
    /// It must NEVER reach a browser or get committed to source control, which is why it
    /// lives in `dotnet user-secrets` for local development instead of appsettings.json.
    /// </summary>
    public string SupabaseServiceRoleKey { get; set; } = string.Empty;

    /// <summary>
    /// How long an IP stays banned after tripping a honeypot, before it is allowed to
    /// make requests again. Kept short by default so the demo can be re-run quickly.
    /// </summary>
    public TimeSpan BanDuration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// When true, the middleware trusts the "X-Forwarded-For" request header as the
    /// caller's IP address instead of the raw socket address. This is what lets us type
    /// `curl -H "X-Forwarded-For: 203.0.113.45" ...` locally and see a realistic public
    /// IP show up in the dashboard instead of always seeing 127.0.0.1.
    ///
    /// In a real production deployment this must only be turned on if you trust the
    /// reverse proxy in front of you to set that header honestly (otherwise an attacker
    /// could simply lie about their own IP to dodge the ban).
    /// </summary>
    public bool TrustForwardedForHeader { get; set; }

    /// <summary>
    /// Path prefixes that are never subject to the ban check, even for an IP that is
    /// currently banned. Without this, banning your own machine's IP (which happens
    /// constantly during local testing, since the dashboard and the "attacker" curl
    /// commands run from the same box) would also lock you out of the dashboard itself.
    /// </summary>
    public string[] ExemptPathPrefixes { get; set; } =
    [
        "/",
        "/index.html",
        "/favicon.ico",
        "/api/dashboard",
    ];

    /// <summary>
    /// The fake routes that look like real vulnerabilities to an attacker's scanner but
    /// do not exist in the real application. Hitting any of these bans the caller's IP
    /// and returns a disguised 404, exactly as if the route genuinely did not exist.
    /// </summary>
    public string[] TrapPaths { get; set; } =
    [
        "/.env",
        "/.git/config",
        "/wp-admin",
        "/wp-login.php",
        "/api/v1/admin/config",
        "/admin/config.php",
        "/phpmyadmin",
        "/actuator/env",
        "/config.json",
        "/backup.sql",
    ];
}

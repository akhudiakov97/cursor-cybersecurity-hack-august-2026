namespace HoneyGuard.Reporting;

/// <summary>
/// A single "someone tripped a honeypot" event, exactly as it will be inserted into the
/// Supabase `incidents` table.
///
/// This is a positional <c>record</c> instead of a class with a constructor and
/// properties written out by hand: records generate that constructor, read-only
/// properties, equality, and a useful ToString() for you from the parameter list alone.
/// That is a good fit here because an Incident is pure data - created once by the
/// middleware and never mutated afterwards.
/// </summary>
/// <param name="IpAddress">The attacker's (resolved) IP address.</param>
/// <param name="Path">The request path that was probed, e.g. "/api/v1/admin/config".</param>
/// <param name="Method">The HTTP method used, e.g. "GET".</param>
/// <param name="UserAgent">The User-Agent header sent by the scanner, if any.</param>
/// <param name="TrapName">Which configured trap path was matched.</param>
/// <param name="OccurredAtUtc">When the trap was tripped.</param>
public sealed record Incident(
    string IpAddress,
    string Path,
    string Method,
    string? UserAgent,
    string TrapName,
    DateTimeOffset OccurredAtUtc);

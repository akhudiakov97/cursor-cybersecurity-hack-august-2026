namespace HoneyGuard.Security;

/// <summary>
/// A single entry in the ban list: which trap the attacker tripped, and when the ban
/// expires. This is declared as a <c>record</c> rather than a <c>class</c> because it is
/// an immutable bag of data with no behaviour of its own - records give us value-based
/// equality and a readable ToString() "for free", which is exactly what a small data
/// holder like this needs.
/// </summary>
/// <param name="TrapPath">The honeypot route that triggered the ban.</param>
/// <param name="BannedAtUtc">When the ban was created.</param>
/// <param name="ExpiresAtUtc">When the ban stops applying.</param>
public sealed record BanRecord(string TrapPath, DateTimeOffset BannedAtUtc, DateTimeOffset ExpiresAtUtc)
{
    /// <summary>
    /// Whether this ban is still in effect. Expiry is checked lazily (on lookup) rather
    /// than with a timer, which keeps <see cref="BanRegistry"/> simple: there is no
    /// background cleanup loop to reason about, just a comparison against the clock.
    /// </summary>
    public bool IsActive(DateTimeOffset now) => now < ExpiresAtUtc;
}

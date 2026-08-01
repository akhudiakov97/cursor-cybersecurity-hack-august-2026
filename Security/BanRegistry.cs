using System.Collections.Concurrent;

namespace HoneyGuard.Security;

/// <summary>
/// The in-memory list of banned IP addresses that gives HoneyGuard its "0ms latency"
/// property: checking whether a request is banned is a single dictionary lookup, with
/// no database round-trip and no network call on the request's hot path.
///
/// This class is registered as a singleton in Program.cs (see
/// <c>builder.Services.AddSingleton&lt;BanRegistry&gt;()</c>), meaning ASP.NET Core creates
/// exactly one instance for the lifetime of the application and hands that same instance
/// to every request. That is essential here: if it were registered as "scoped" or
/// "transient" instead, every request would get its own empty ban list and nothing would
/// ever actually stay banned.
///
/// Because a singleton is shared across every concurrent request, it must be safe to
/// read and write from many threads at once. <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// gives us that thread safety without our having to write any manual locking code.
/// </summary>
public sealed class BanRegistry
{
    private readonly ConcurrentDictionary<string, BanRecord> _bannedIpAddresses = new();

    /// <summary>
    /// Bans an IP address for <paramref name="duration"/>, recording which trap it hit.
    /// If the IP was already banned, this simply refreshes the ban with the newest trap
    /// and expiry.
    /// </summary>
    public void Ban(string ipAddress, string trapPath, TimeSpan duration)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BanRecord record = new(trapPath, BannedAtUtc: now, ExpiresAtUtc: now.Add(duration));

        // The indexer performs an atomic add-or-update, which is what makes this safe to
        // call from many requests at the same time without extra locking.
        _bannedIpAddresses[ipAddress] = record;
    }

    /// <summary>
    /// Returns true if <paramref name="ipAddress"/> currently has an active ban. Expired
    /// bans are treated as "not banned" here and are lazily removed from the dictionary,
    /// so the registry does not grow forever without a separate cleanup timer.
    /// </summary>
    public bool IsBanned(string ipAddress)
    {
        if (!_bannedIpAddresses.TryGetValue(ipAddress, out BanRecord? record))
        {
            return false;
        }

        if (record.IsActive(DateTimeOffset.UtcNow))
        {
            return true;
        }

        // The ban has expired - remove it so a future scan of the dictionary (or the
        // dashboard reset below) does not have to look at stale entries. TryRemove takes
        // the expected value so we don't accidentally delete a *newer* ban that another
        // request might have just written for the same IP.
        _bannedIpAddresses.TryRemove(new KeyValuePair<string, BanRecord>(ipAddress, record));
        return false;
    }

    /// <summary>
    /// Removes every ban. Exposed only so the dashboard's "reset demo" button
    /// (see Endpoints/DashboardEndpoints.cs) can put the app back into a clean state
    /// between takes without restarting the process.
    /// </summary>
    public void Clear() => _bannedIpAddresses.Clear();
}

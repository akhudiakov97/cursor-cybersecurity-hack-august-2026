using System.Threading.Channels;

namespace HoneyGuard.Reporting;

/// <summary>
/// A thread-safe, in-memory hand-off point between the request pipeline (which produces
/// incidents) and <see cref="IncidentDispatcher"/> (which consumes them and talks to
/// Supabase in the background).
///
/// Why not just call Supabase directly from the middleware with
/// <c>_ = Task.Run(() => PostToSupabaseAsync(incident))</c>? That "fire and forget" pattern
/// is a common beginner mistake in .NET: the task is not tracked by anything, so an
/// exception inside it disappears silently instead of being logged, and there is no way
/// to slow down or apply back-pressure if incidents arrive faster than Supabase can
/// accept them. A <see cref="Channel{T}"/> solves both problems: it is a queue built for
/// exactly this producer/consumer hand-off, one side calls <see cref="TryReport"/> and
/// moves on immediately, the other side (the dispatcher) awaits new items in a loop where
/// exceptions can be caught and logged properly.
///
/// Registered as a singleton in Program.cs because the queue itself, like
/// <see cref="Security.BanRegistry"/>, needs to be the same instance shared by every
/// request and by the single background dispatcher.
/// </summary>
public sealed class IncidentQueue
{
    /// <summary>
    /// Bounded to a small capacity with <see cref="BoundedChannelFullMode.DropWrite"/>:
    /// if Supabase is briefly unreachable and the queue fills up, we deliberately drop
    /// the newest incidents rather than let the queue grow without limit or block the
    /// request pipeline while waiting for room. Losing a duplicate "scanner is probing
    /// us" event during an outage is an acceptable trade-off for never slowing down a
    /// real request.
    /// </summary>
    private readonly Channel<Incident> _channel = Channel.CreateBounded<Incident>(
        new BoundedChannelOptions(capacity: 256)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });

    /// <summary>
    /// Enqueues an incident without ever blocking or throwing. Called directly from the
    /// request-handling path in <see cref="Security.HoneyGuardMiddleware"/>, so it must
    /// return immediately - this is what keeps trap detection "0ms latency" even though
    /// the eventual Supabase write can take tens of milliseconds.
    /// </summary>
    public void TryReport(Incident incident) => _channel.Writer.TryWrite(incident);

    /// <summary>
    /// The read side of the queue, used only by <see cref="IncidentDispatcher"/> to pull
    /// incidents off one at a time as they arrive.
    /// </summary>
    public ChannelReader<Incident> Reader => _channel.Reader;
}

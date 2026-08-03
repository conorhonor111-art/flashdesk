using System.Collections.Concurrent;

namespace RemoteDesktop.Relay;

/// <summary>
/// A small sliding-window limiter, per caller IP.
///
/// It exists because the relay is now open to strangers: nine digits is a billion combinations,
/// which sounds like a lot and is not, so an unthrottled server invites someone to sweep the
/// number space looking for machines that are switched on. This does not make the ID a secret —
/// the allow-list and the consent dialog are what protect a machine (CLAUDE.md "the ID is an
/// ADDRESS, not a credential") — it makes sweeping slow and visible instead of free.
///
/// Deliberately in memory: a restart forgetting recent counts is harmless, and it keeps the relay
/// with no database to run, back up or leak.
/// </summary>
public sealed class RateLimiter
{
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, Window> _callers = new();

    private sealed class Window
    {
        public DateTimeOffset StartUtc;
        public int Count;
    }

    public RateLimiter(int limit, TimeSpan window)
    {
        _limit = limit;
        _window = window;
    }

    /// <summary>Total requests turned away since start — surfaced on the usage page.</summary>
    public long Rejected;

    public bool Allow(string caller)
    {
        var now = DateTimeOffset.UtcNow;
        var window = _callers.GetOrAdd(caller, _ => new Window { StartUtc = now });

        lock (window)
        {
            if (now - window.StartUtc > _window)
            {
                window.StartUtc = now;
                window.Count = 0;
            }

            if (window.Count >= _limit)
            {
                Interlocked.Increment(ref Rejected);
                return false;
            }

            window.Count++;
            return true;
        }
    }

    /// <summary>Drops callers whose window has long expired, so memory cannot grow without bound.</summary>
    public void Sweep()
    {
        var cutoff = DateTimeOffset.UtcNow - _window - _window;
        foreach (var kv in _callers)
            if (kv.Value.StartUtc < cutoff)
                _callers.TryRemove(kv.Key, out _);
    }

    public int TrackedCallers => _callers.Count;
}

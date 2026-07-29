using System.Diagnostics;

namespace RemoteDesktop.Shared.Diagnostics;

/// <summary>
/// Counts frames and bytes over the most recent one-second window, producing the frames-per-second
/// and bytes-per-second figures the windows display. Thread-safe: the network loop records into it
/// while the UI thread reads from it.
/// </summary>
public sealed class RateMeter
{
    private static readonly long WindowTicks = Stopwatch.Frequency; // one second, in Stopwatch ticks

    private readonly object _gate = new();
    private readonly Queue<Sample> _samples = new();

    private readonly record struct Sample(long Timestamp, int Frames, long Bytes);

    /// <summary>Record activity: some number of frames and bytes that just happened.</summary>
    public void Record(int frames, long bytes)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _samples.Enqueue(new Sample(now, frames, bytes));
            Trim(now);
        }
    }

    /// <summary>Current rates over the last second. Because the window is exactly one second, the
    /// windowed sums are already per-second rates.</summary>
    public (double FramesPerSecond, double BytesPerSecond) Read()
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            Trim(now);
            int frames = 0;
            long bytes = 0;
            foreach (var s in _samples)
            {
                frames += s.Frames;
                bytes += s.Bytes;
            }
            return (frames, bytes);
        }
    }

    private void Trim(long now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().Timestamp > WindowTicks)
            _samples.Dequeue();
    }
}

using System.Diagnostics;

namespace RemoteDesktop.Shared.Diagnostics;

/// <summary>
/// Where a live session's frame time actually goes, broken into the four stages the frame loop
/// spends it on: capture, diff, encode, send. These are the live session's OWN cost breakdown —
/// not a synthetic benchmark — so the four figures should add up to roughly the frame time, and
/// whichever one is largest is the thing to fix. "Encode" includes assembling the frame packet,
/// because both steps do the same job: turning pixels into the bytes that go on the wire.
///
/// The window is the same one second the RateMeter uses, so these numbers describe the same second
/// as the frames-per-second and KB/s figures already on screen. Thread-safe: the frame loop records
/// into it while the UI thread reads from it.
/// </summary>
public sealed class FrameTimings
{
    private static readonly long WindowTicks = Stopwatch.Frequency; // one second, in Stopwatch ticks

    private readonly object _gate = new();
    private readonly Queue<Sample> _samples = new();

    private readonly record struct Sample(long Timestamp, long Capture, long Diff, long Encode, long Send);

    /// <summary>Record one frame's stage costs, each in Stopwatch ticks.</summary>
    public void Record(long captureTicks, long diffTicks, long encodeTicks, long sendTicks)
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            _samples.Enqueue(new Sample(now, captureTicks, diffTicks, encodeTicks, sendTicks));
            Trim(now);
        }
    }

    /// <summary>
    /// Average milliseconds per frame for each stage over the last second, and how many frames that
    /// average is made of. An empty window reads as all zeros rather than dividing by nothing.
    /// </summary>
    public (double CaptureMs, double DiffMs, double EncodeMs, double SendMs, int Frames) Read()
    {
        long now = Stopwatch.GetTimestamp();
        lock (_gate)
        {
            Trim(now);
            int frames = _samples.Count;
            if (frames == 0) return (0, 0, 0, 0, 0);

            long capture = 0, diff = 0, encode = 0, send = 0;
            foreach (var s in _samples)
            {
                capture += s.Capture;
                diff += s.Diff;
                encode += s.Encode;
                send += s.Send;
            }

            double perFrame = 1000.0 / (Stopwatch.Frequency * (double)frames); // ticks -> ms, averaged
            return (capture * perFrame, diff * perFrame, encode * perFrame, send * perFrame, frames);
        }
    }

    private void Trim(long now)
    {
        while (_samples.Count > 0 && now - _samples.Peek().Timestamp > WindowTicks)
            _samples.Dequeue();
    }
}

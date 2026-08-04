using System.Diagnostics;
using System.Text;

namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// Watches one live session and writes down what a person would otherwise have to read off the
/// screen while it happened.
///
/// The reason this exists is the same rule that produced the diagnostics runner: a person on a
/// phone call is a bad measuring instrument. Asking a tester to watch four numbers during a support
/// session gets you four guesses. So the program measures itself, and the instruction to a tester
/// becomes "send me this file".
///
/// Everything here is per session and thread-simple: the frame loop writes, the UI thread reads
/// once at the end, and a single lock covers both.
/// </summary>
public sealed class SessionRecorder
{
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private string _peerId = "";
    private string _captureMethod = "";
    private int _width, _height;

    private long _frames;
    private long _totalBytes;

    // Frame rate is judged per whole second, not per frame. One slow frame is not a slow session,
    // and "worst fps" has to mean a second a person would actually have felt.
    private long _currentSecond = -1;
    private long _framesThisSecond;
    private int _worstFps = int.MaxValue;

    private double _sumCapture, _sumDiff, _sumEncode, _sumSend;
    private long _stageSamples;
    private double _worstFrameMs;

    private int _highestLevel;
    private int _lastLevel;
    private long _lastLevelAtMs;
    private long _msAboveLevel0;

    private int _reconnects;
    private long _reconnectMsTotal;

    public void Begin(string peerId, string captureMethod, int width, int height)
    {
        lock (_gate)
        {
            _peerId = peerId;
            _captureMethod = captureMethod;
            _width = width;
            _height = height;
            _lastLevelAtMs = _clock.ElapsedMilliseconds;
        }
    }

    /// <summary>Called once per frame from the frame loop, with what that frame cost.</summary>
    public void RecordFrame(int bytes, int level, double captureMs, double diffMs, double encodeMs, double sendMs)
    {
        lock (_gate)
        {
            long nowMs = _clock.ElapsedMilliseconds;

            _frames++;
            _totalBytes += bytes;

            long second = nowMs / 1000;
            if (_currentSecond < 0) _currentSecond = second;
            while (second > _currentSecond)
            {
                // A completed second. The very first and last seconds are partial, so they are not
                // allowed to set the worst figure — they would report a slow second that never was.
                if (_currentSecond > 0) _worstFps = Math.Min(_worstFps, (int)_framesThisSecond);
                _framesThisSecond = 0;
                _currentSecond++;
            }
            _framesThisSecond++;

            _sumCapture += captureMs;
            _sumDiff += diffMs;
            _sumEncode += encodeMs;
            _sumSend += sendMs;
            _stageSamples++;
            _worstFrameMs = Math.Max(_worstFrameMs, captureMs + diffMs + encodeMs + sendMs);

            // Time spent below full speed is attributed to the level that was in force during it.
            if (_lastLevel > 0) _msAboveLevel0 += nowMs - _lastLevelAtMs;
            _lastLevelAtMs = nowMs;
            _lastLevel = level;
            _highestLevel = Math.Max(_highestLevel, level);
        }
    }

    /// <summary>A dropped link that came back without the client being asked again.</summary>
    public void RecordReconnect(TimeSpan gap)
    {
        lock (_gate)
        {
            _reconnects++;
            _reconnectMsTotal += (long)gap.TotalMilliseconds;
        }
    }

    /// <summary>
    /// The plain-text block appended to the session log. Deliberately readable by someone who has
    /// never seen the program's internals — every line says what it means, and the units are stated.
    /// </summary>
    public string Report(int ladderSize)
    {
        lock (_gate)
        {
            double seconds = Math.Max(0.001, _clock.Elapsed.TotalSeconds);
            double avgFps = _frames / seconds;
            double avgKbPerSec = _totalBytes / 1024.0 / seconds;
            int worstFps = _worstFps == int.MaxValue ? (int)Math.Round(avgFps) : _worstFps;

            var b = new StringBuilder();
            b.AppendLine("  --- how it went ---");
            b.AppendLine($"    Lasted         : {Duration(_clock.Elapsed)}");
            b.AppendLine($"    Screen         : {_width} x {_height}, captured with {_captureMethod}");
            b.AppendLine($"    Picture        : {avgFps:0.0} frames per second average, worst second {worstFps}");
            b.AppendLine($"    Sent           : {_totalBytes / 1024.0 / 1024.0:0.0} MB total, {avgKbPerSec:0.0} KB per second average");

            if (_highestLevel == 0)
            {
                b.AppendLine($"    Speed limiting : never needed (stayed at full speed the whole session)");
            }
            else
            {
                double share = 100.0 * _msAboveLevel0 / (seconds * 1000.0);
                b.AppendLine($"    Speed limiting : reached step {_highestLevel} of {ladderSize - 1}");
                b.AppendLine($"                     spent {Duration(TimeSpan.FromMilliseconds(_msAboveLevel0))} below full speed ({share:0}% of the session)");
            }

            if (_stageSamples > 0)
            {
                b.AppendLine($"    Where the time went (average per frame, milliseconds):");
                b.AppendLine($"                     capture {_sumCapture / _stageSamples:0.0}  compare {_sumDiff / _stageSamples:0.0}  " +
                             $"compress {_sumEncode / _stageSamples:0.0}  send {_sumSend / _stageSamples:0.0}");
                b.AppendLine($"                     slowest single frame {_worstFrameMs:0} ms");
            }

            b.AppendLine(_reconnects == 0
                ? "    Interruptions  : none"
                : $"    Interruptions  : {_reconnects} — the link dropped and came back on its own " +
                  $"(about {Duration(TimeSpan.FromMilliseconds(_reconnectMsTotal))} in total)");

            b.Append("  --- end ---");
            return b.ToString();
        }
    }

    private static string Duration(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
            : $"{span.TotalSeconds:0}s";
}

using System.Diagnostics;
using System.Linq;
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

    // One entry per completed second: what the picture actually did during it. Kept so a file
    // transfer's before/during/after can be answered from history already recorded, instead of
    // asking a tester to watch the technical view at the exact moment a transfer happens.
    //
    // VideoBytes/FileBytes were added 2026-08-27, same reasoning as HostServer.FileMeter: "Sent" on
    // its own was silently video-only, and a tester watching the live window to get the file/video
    // split is exactly the "someone has to watch a window that pollutes it" problem this class exists
    // to remove. See PROGRESS.md, "The 27 KB/s reading was never the file".
    // Level added 2026-08-27: quality alone cannot answer "did it get all the way back", because
    // the ladder holds quality at 95 across levels 0-4 (see BandwidthGovernor's own table) — a
    // session sitting at level 4 would read as "quality recovered" while still 4 steps short of
    // full speed. The level actually in force is the only figure that can settle "recovered, or
    // stuck at a lesser speed" the way Conor asked for, from the log, without anyone watching a
    // window while it happens.
    private readonly record struct SecondBucket(long Second, int Frames, double AvgQuality, long VideoBytes, long FileBytes, int Level);
    private readonly List<SecondBucket> _seconds = new();
    private double _qualitySumThisSecond;
    private long _videoBytesThisSecond;
    private long _fileBytesThisSecond;
    private int _levelThisSecond;

    // A transfer is "one at a time" by the file service's own rule (HostFileService._transferBusy),
    // so a plain list of windows is enough — no need to track which transfer a frame belongs to.
    private sealed class TransferWindow
    {
        public long StartSecond;
        public long? EndSecond;
        // Precise, code-level timestamps — what Conor asked to see "from the code, not from when I
        // clicked" (2026-08-27). The whole-second window above is what the before/during/after
        // averaging already used; these are for stating the true duration exactly.
        public DateTimeOffset StartedAtUtc;
        public DateTimeOffset? EndedAtUtc;
    }
    private readonly List<TransferWindow> _transfers = new();
    private TransferWindow? _activeTransfer;

    // Seconds of picture either side of a transfer counted as its "before" and "after". Five is
    // enough to average out one noisy frame without reaching into whatever happened long before.
    private const int TransferWindowSeconds = 5;

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
    public void RecordFrame(int bytes, int level, int quality, double captureMs, double diffMs, double encodeMs, double sendMs)
    {
        lock (_gate)
        {
            long nowMs = _clock.ElapsedMilliseconds;

            _frames++;
            _totalBytes += bytes;

            long second = nowMs / 1000;
            if (_currentSecond < 0) _currentSecond = second;
            RollSecondsTo(second);
            _framesThisSecond++;
            _qualitySumThisSecond += quality;
            _videoBytesThisSecond += bytes;
            _levelThisSecond = level; // last frame in the second wins — same granularity as AvgQuality

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

    /// <summary>
    /// A file chunk just went out or arrived. Called from the same point HostServer's FileMeter is,
    /// so the session log and the technical view are reading the same measurement, not two different
    /// ones that could quietly drift apart.
    /// </summary>
    public void RecordFileBytes(int bytes)
    {
        lock (_gate)
        {
            long nowMs = _clock.ElapsedMilliseconds;
            long second = nowMs / 1000;
            if (_currentSecond < 0) _currentSecond = second;
            RollSecondsTo(second);
            _fileBytesThisSecond += bytes;
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
    /// A file transfer just started moving bytes. Called from HostFileService at the same point
    /// _transferBusy is claimed, so "during" always means bytes were actually in flight.
    /// </summary>
    public void BeginTransfer()
    {
        lock (_gate)
        {
            RollSecondsTo(_clock.ElapsedMilliseconds / 1000);
            var window = new TransferWindow { StartSecond = _currentSecond, StartedAtUtc = DateTimeOffset.UtcNow };
            _transfers.Add(window);
            _activeTransfer = window;
        }
    }

    /// <summary>The transfer above finished, one way or another — done, refused, or cut off.</summary>
    public void EndTransfer()
    {
        lock (_gate)
        {
            RollSecondsTo(_clock.ElapsedMilliseconds / 1000);
            if (_activeTransfer is not null)
            {
                _activeTransfer.EndSecond = _currentSecond;
                _activeTransfer.EndedAtUtc = DateTimeOffset.UtcNow;
            }
            _activeTransfer = null;
        }
    }

    /// <summary>
    /// Closes out every completed second up to (not including) <paramref name="second"/>, filing each
    /// one's frame count and average quality away in <see cref="_seconds"/>. Called from RecordFrame as
    /// the clock advances, and from Report/BeginTransfer/EndTransfer so a caller reading the history
    /// mid-session sees it brought up to date rather than stalled at the last frame that happened to
    /// land on a whole second.
    /// </summary>
    private void RollSecondsTo(long second)
    {
        while (second > _currentSecond)
        {
            // The very first second is partial (the session did not start on a whole second), so it
            // is not allowed to set the worst-fps figure — same reasoning the original code used.
            // It is still filed into _seconds so a transfer starting in the first second has SOME
            // "before" data rather than none.
            if (_currentSecond > 0) _worstFps = Math.Min(_worstFps, (int)_framesThisSecond);
            _seconds.Add(new SecondBucket(_currentSecond, (int)_framesThisSecond,
                _framesThisSecond > 0 ? _qualitySumThisSecond / _framesThisSecond : 0,
                _videoBytesThisSecond, _fileBytesThisSecond, _levelThisSecond));
            _framesThisSecond = 0;
            _qualitySumThisSecond = 0;
            _videoBytesThisSecond = 0;
            _fileBytesThisSecond = 0;
            _currentSecond++;
        }
    }

    /// <summary>
    /// The plain-text block appended to the session log. Deliberately readable by someone who has
    /// never seen the program's internals — every line says what it means, and the units are stated.
    /// </summary>
    /// <summary>
    /// Seconds during which this machine could not see its own screen, so nothing was sent. Counted
    /// separately because a session that spent half its life behind a lock screen is not a session
    /// with poor throughput — and reporting it as one sends the reader to the wrong problem.
    /// </summary>
    public void RecordUnavailable(double seconds)
    {
        lock (_gate) { _unavailableSeconds += seconds; }
    }

    private double _unavailableSeconds;

    /// <summary>
    /// One short line — enough to know the session is alive and roughly how it is going. For a
    /// periodic checkpoint (see <c>HostServer.EmitPeriodicCheckpoint</c>) that must not turn into the
    /// "fourteen near-identical blocks" problem <see cref="Report"/>'s own full text would create if
    /// written every few minutes for hours. Added 2026-08-27, per Conor's own instinct: the full
    /// report stays reserved for the one block that matters — the true end.
    /// </summary>
    public string ShortStatusLine()
    {
        lock (_gate)
        {
            if (_currentSecond >= 0) RollSecondsTo(_clock.ElapsedMilliseconds / 1000);
            double seconds = Math.Max(0.001, _clock.Elapsed.TotalSeconds);
            double sendingSeconds = Math.Max(0.001, seconds - _unavailableSeconds);
            double avgFps = _frames / sendingSeconds;
            double avgKbPerSec = _totalBytes / 1024.0 / sendingSeconds;
            return $"{Duration(_clock.Elapsed)} so far, {avgFps:0.0} fps avg, {avgKbPerSec:0.0} KB/s avg, "
                 + $"level {_lastLevel} now (reached {_highestLevel} at worst), {_transfers.Count} file transfer(s)";
        }
    }

    /// <summary>
    /// Just the most recently completed transfer's own before/during/after/recovery lines — not the
    /// whole report repeated. For a checkpoint written the moment a transfer ends (see
    /// <c>HostServer.EmitTransferCheckpoint</c>), which is exactly when that data becomes worth
    /// having on disk even if nothing else ever is. Null if no transfer has finished yet.
    /// </summary>
    public string? LastTransferLine(int ladderTop)
    {
        lock (_gate)
        {
            int i = _transfers.Count - 1;
            while (i >= 0 && _transfers[i].EndSecond is null) i--; // skip one still in progress
            if (i < 0) return null;

            RollSecondsTo(_clock.ElapsedMilliseconds / 1000);
            var b = new StringBuilder();
            AppendOneTransfer(b, i, ladderTop);
            return b.ToString().TrimEnd('\r', '\n');
        }
    }

    public string Report(int ladderSize)
    {
        lock (_gate)
        {
            // Roll the per-second buckets forward to NOW before reading them. Without this, seconds
            // in which NO frame arrived never completed, so the worst second stayed at whatever the
            // busy opening second was — one report showed a worst second of 15 alongside an average
            // of 3.0, which cannot happen: the minimum can never exceed the mean. The same rollover
            // also files away whatever "after a transfer" seconds have elapsed by report time.
            if (_currentSecond >= 0) RollSecondsTo(_clock.ElapsedMilliseconds / 1000);

            double seconds = Math.Max(0.001, _clock.Elapsed.TotalSeconds);
            double sendingSeconds = Math.Max(0.001, seconds - _unavailableSeconds);
            double avgFps = _frames / sendingSeconds;
            double avgKbPerSec = _totalBytes / 1024.0 / sendingSeconds;
            int worstFps = _worstFps == int.MaxValue ? (int)Math.Round(avgFps) : _worstFps;

            var b = new StringBuilder();
            b.AppendLine("  --- how it went ---");
            b.AppendLine($"    Lasted         : {Duration(_clock.Elapsed)}");
            b.AppendLine($"    Screen         : {_width} x {_height}, captured with {_captureMethod}");
            b.AppendLine($"    Picture        : {avgFps:0.0} frames per second average, worst second {worstFps}");
            b.AppendLine($"    Sent           : {_totalBytes / 1024.0 / 1024.0:0.0} MB total, {avgKbPerSec:0.0} KB per second average");
            if (_unavailableSeconds >= 1)
                b.AppendLine($"    Screen hidden  : {Duration(TimeSpan.FromSeconds(_unavailableSeconds))} of that time the screen "
                           + "could not be seen at all (locked, a Windows prompt, or a screensaver)."
                           + Environment.NewLine
                           + "                     Nothing was sent then, and the rates above exclude it.");

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

            AppendTransfers(b, ladderSize - 1);

            b.Append("  --- end ---");
            return b.ToString();
        }
    }

    /// <summary>
    /// Answers, from what was already recorded, whether moving a file actually slowed the picture —
    /// the claim itself was never measured before this. One line per transfer: what the picture was
    /// doing in the five seconds before it started, for its whole duration, and in the five seconds
    /// after it ended. Must be called with _gate already held.
    /// </summary>
    private void AppendTransfers(StringBuilder b, int ladderTop)
    {
        if (_transfers.Count == 0) return;

        b.AppendLine($"    File transfers : {_transfers.Count}, effect on the picture measured, not assumed");
        for (int i = 0; i < _transfers.Count; i++)
            AppendOneTransfer(b, i, ladderTop);
    }

    /// <summary>
    /// One transfer's before/during/after/recovery lines — the body <see cref="AppendTransfers"/>
    /// loops over. Pulled out on its own so a CHECKPOINT (see <see cref="LastTransferLine"/>) can
    /// write just the one transfer that just happened, not the whole report repeated. Must be called
    /// with _gate already held.
    /// </summary>
    private void AppendOneTransfer(StringBuilder b, int i, int ladderTop)
    {
        var t = _transfers[i];
        var beforeSeconds = _seconds.Where(s => s.Second >= t.StartSecond - TransferWindowSeconds && s.Second < t.StartSecond).ToList();
        string before = SummarizeWindow(beforeSeconds);
        long duringEnd = t.EndSecond ?? _currentSecond;
        var duringSeconds = _seconds.Where(s => s.Second >= t.StartSecond && s.Second < duringEnd).ToList();
        string during = SummarizeWindow(duringSeconds);
        string after;
        if (t.EndSecond is long endSecond)
        {
            var afterSeconds = _seconds.Where(s => s.Second >= endSecond && s.Second < endSecond + TransferWindowSeconds).ToList();
            after = SummarizeWindow(afterSeconds);
            after += " — " + RecoveryLine(endSecond, ladderTop);
        }
        else
        {
            after = "n/a — still moving when the session ended";
        }

        string span = t.EndedAtUtc is { } endedAt
            ? $"{t.StartedAtUtc.ToLocalTime():HH:mm:ss.fff} to {endedAt.ToLocalTime():HH:mm:ss.fff} ({(endedAt - t.StartedAtUtc).TotalSeconds:0.0}s)"
            : $"started {t.StartedAtUtc.ToLocalTime():HH:mm:ss.fff}, still moving";
        b.AppendLine($"                     #{i + 1}  {span}");
        b.AppendLine($"                          before: {before}");
        b.AppendLine($"                          during: {during}" +
                     (t.EndSecond is null ? " (still in progress when the session ended)" : ""));
        b.AppendLine($"                          after : {after}");
    }

    /// <summary>
    /// Average fps, quality, and the file/video byte split across a set of completed seconds — the
    /// same figures the technical view shows live, read from history instead, so nobody has to watch
    /// a window to get them (and watching it would have changed what it measured — see
    /// PROGRESS.md, 2026-08-27).
    /// </summary>
    private static string SummarizeWindow(List<SecondBucket> seconds)
    {
        if (seconds.Count == 0) return "not enough of the session either side to say";
        double avgFps = seconds.Average(s => s.Frames);
        double avgQuality = seconds.Average(s => s.AvgQuality);
        double videoKBps = seconds.Average(s => s.VideoBytes) / 1024.0;
        double fileKBps = seconds.Average(s => s.FileBytes) / 1024.0;
        string bytes = fileKBps > 0.05
            ? $", file {fileKBps:0.0} KB/s + video {videoKBps:0.0} KB/s = {fileKBps + videoKBps:0.0} KB/s total"
            : $", {videoKBps:0.0} KB/s";
        return $"{avgFps:0.0} fps, quality {avgQuality:0}{bytes} (over {seconds.Count}s)";
    }

    /// <summary>
    /// How many seconds after a transfer ended it took the LADDER to reach level 0 — full speed —
    /// or, if it never did within what was recorded, where it got stuck. Answers Conor's "confirm it
    /// recovers, and how long that takes" (2026-08-27) from recorded history instead of a stopwatch
    /// on the technical view.
    ///
    /// <para>Deliberately checks the LEVEL, not quality: the ladder holds quality at 95 across levels
    /// 0-4 (see <see cref="BandwidthGovernor"/>'s own table), so a session sitting at level 4 would
    /// read as "quality recovered" while still four steps short of full speed — exactly the gap that
    /// made the first live run's "stalled around level 5" finding impossible to state precisely from
    /// quality alone.</para>
    /// </summary>
    private string RecoveryLine(long endSecond, int ladderTop)
    {
        for (long s = endSecond; s <= _currentSecond; s++)
        {
            var bucket = _seconds.FirstOrDefault(b => b.Second == s);
            if (bucket.Frames > 0 && bucket.Level == 0)
                return s == endSecond ? "back to full speed (level 0) immediately" : $"back to full speed (level 0) after {s - endSecond}s";
        }
        var lastSeen = _seconds.Where(b => b.Second >= endSecond && b.Frames > 0)
            .OrderByDescending(b => b.Second).FirstOrDefault();
        string stuckAt = lastSeen.Frames > 0 ? $"level {lastSeen.Level}/{ladderTop}" : "no frames recorded since";
        return $"NOT back to full speed within {_currentSecond - endSecond}s and counting — stuck at {stuckAt}";
    }

    private static string Duration(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
            : $"{span.TotalSeconds:0}s";
}

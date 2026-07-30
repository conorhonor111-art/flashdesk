using System.Diagnostics;

namespace RemoteDesktop.Host.Capture;

/// <summary>
/// The program's own record of whether screen capture stayed healthy — so the operator (or a client
/// on the phone) never has to watch four indicators through a screen lock and a UAC prompt. It counts
/// interruptions that recovered vs. ones that did not, and writes every event to a plain-text file
/// with timestamps, cause, recovery time, and the capture method before and after.
///
/// Thread-safe: the capture thread reports events while the UI thread reads the counters.
/// </summary>
public sealed class CaptureHealthLog
{
    private readonly object _gate = new();
    private long _interruptedAt; // Stopwatch timestamp; 0 = not currently interrupted
    private CaptureMethod _methodBefore;

    public int Survived { get; private set; }
    public int Failures { get; private set; }
    public string LogPath { get; }

    public CaptureHealthLog()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(dir)) dir = AppContext.BaseDirectory;
        LogPath = Path.Combine(dir, "RemoteDesktop-capture-log.txt");
        Write($"=== Capture log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
    }

    /// <summary>Capture was just lost. Ignored if an interruption is already in progress.</summary>
    public void Interrupted(CaptureMethod method, string reason)
    {
        lock (_gate)
        {
            if (_interruptedAt != 0) return;
            _interruptedAt = Stopwatch.GetTimestamp();
            _methodBefore = method;
            Write($"INTERRUPTED   capture={method}  reason={reason}");
        }
    }

    /// <summary>Capture is working again. No-op unless we were interrupted.</summary>
    public void Recovered(CaptureMethod method)
    {
        lock (_gate)
        {
            if (_interruptedAt == 0) return;
            double ms = ElapsedMs();
            _interruptedAt = 0;
            Survived++;
            Write($"RECOVERED     after {ms:0} ms   {_methodBefore} -> {method}   (survived={Survived})");
        }
    }

    /// <summary>Capture could not recover on the same path and fell back (DXGI -> GDI). Counts as a failure.</summary>
    public void FellBack(CaptureMethod from, CaptureMethod to)
    {
        lock (_gate)
        {
            double ms = _interruptedAt == 0 ? 0 : ElapsedMs();
            _interruptedAt = 0;
            Failures++;
            Write($"DID NOT RECOVER  {from} gave up after {ms:0} ms; fell back to {to}   (failures={Failures})");
        }
    }

    private double ElapsedMs() => (Stopwatch.GetTimestamp() - _interruptedAt) * 1000.0 / Stopwatch.Frequency;

    private void Write(string line)
    {
        try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}"); }
        catch { /* logging must never take the session down */ }
    }
}

namespace RemoteDesktop.Host.Capture;

/// <summary>
/// Wraps the real capture so a DXGI failure can never crash a session. It runs the inner capture
/// (DXGI when possible); if DXGI throws — having exhausted its own recovery — this switches to GDI for
/// the rest of the session, records the failure in the <see cref="CaptureHealthLog"/>, and keeps
/// going. Because the window reads the capture method live, the display updates itself when a switch
/// happens.
/// </summary>
public sealed class ResilientScreenCapture : IScreenCapture
{
    private readonly CaptureHealthLog? _health;
    private IScreenCapture _inner;

    public ResilientScreenCapture(IScreenCapture inner, CaptureHealthLog? health = null)
    {
        _inner = inner;
        _health = health;
    }

    public CaptureMethod Method => _inner.Method;
    public int Width => _inner.Width;
    public int Height => _inner.Height;

    /// <summary>
    /// True when the last attempt THREW — Windows would not let this machine see its own screen.
    /// A false return with this still false means the screen was simply unchanged, which is normal
    /// and must not be reported to anyone as a problem.
    /// </summary>
    public bool ScreenUnavailable { get; private set; }

    public bool TryCapture(int timeoutMilliseconds, out CapturedFrame frame)
    {
        try
        {
            bool got = _inner.TryCapture(timeoutMilliseconds, out frame);
            ScreenUnavailable = false;
            return got;
        }
        catch when (_inner.Method == CaptureMethod.Dxgi)
        {
            // DXGI has given up. Fall back to GDI and keep the session alive.
            frame = default;
            ScreenUnavailable = true;
            _health?.FellBack(CaptureMethod.Dxgi, CaptureMethod.Gdi);
            var dead = _inner;
            _inner = NewGdi();
            try { dead.Dispose(); } catch { /* ignore */ }
            return false; // the next tick captures on GDI
        }
        catch
        {
            // ⚠️ EVERY OTHER PATH — in practice GDI, after a fallback has already happened.
            //
            // This catch was missing, and its absence ended sessions. The filter above reads
            // `when (_inner.Method == CaptureMethod.Dxgi)`, and the moment the wrapper falls back
            // that condition stops being true — so the guard evaporated at exactly the moment it
            // was needed, and a GDI failure escaped into the frame loop and killed the session.
            // Observed as a session reconnecting every two seconds, forever, sending nothing.
            //
            // Windows legitimately refuses screen capture on a locked desktop, a screensaver, a
            // user-account-control password prompt, and while switching users. Those are ordinary
            // moments in a support session, not crashes, and none of them may end it.
            frame = default;
            ScreenUnavailable = true;
            return false;
        }
    }

    private static GdiScreenCapture NewGdi()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        return new GdiScreenCapture(bounds.Width, bounds.Height);
    }

    public void Dispose() => _inner.Dispose();
}

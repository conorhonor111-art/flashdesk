namespace RemoteDesktop.Host.Capture;

/// <summary>
/// Wraps the real capture so a DXGI failure can never crash a session. It runs the inner capture
/// (DXGI when possible); if DXGI throws — having exhausted its own recovery — this switches to GDI for
/// the rest of the session instead of letting the exception tear the session down. Because the window
/// reads the capture method live, the display updates itself when a switch happens.
/// </summary>
public sealed class ResilientScreenCapture : IScreenCapture
{
    private IScreenCapture _inner;

    public ResilientScreenCapture(IScreenCapture inner) => _inner = inner;

    public CaptureMethod Method => _inner.Method;
    public int Width => _inner.Width;
    public int Height => _inner.Height;

    public bool TryCapture(int timeoutMilliseconds, out CapturedFrame frame)
    {
        try
        {
            return _inner.TryCapture(timeoutMilliseconds, out frame);
        }
        catch when (_inner.Method == CaptureMethod.Dxgi)
        {
            // DXGI has given up. Fall back to GDI and keep the session alive.
            frame = default;
            var dead = _inner;
            _inner = NewGdi();
            try { dead.Dispose(); } catch { /* ignore */ }
            return false; // the next tick captures on GDI
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

namespace RemoteDesktop.Host.Capture;

/// <summary>Which capture technique produced the frames.</summary>
public enum CaptureMethod
{
    Dxgi,
    Gdi,
}

/// <summary>
/// A source of screen frames. Implemented twice — DXGI Desktop Duplication (fast, needs a real GPU)
/// and GDI BitBlt (slower, works everywhere). The rest of the host never needs to know which one is
/// running; it just calls TryCapture. This interface is the seam that lets Stage 6 swap in
/// session-aware capture later.
/// </summary>
public interface IScreenCapture : IDisposable
{
    CaptureMethod Method { get; }
    int Width { get; }
    int Height { get; }

    /// <summary>
    /// Where the captured screen's top-left corner sits on the whole virtual desktop.
    ///
    /// <para>Zero on a single-screen machine, which is why nothing needed it before. On a machine
    /// with two screens it is what turns a pixel in the picture into a point Windows can be told to
    /// click — and it is NEGATIVE for a screen placed to the left of or above the primary. Without
    /// it the injection has to assume zero, and every click on such a screen lands on the primary
    /// instead, which looks like a coordinate bug and is not.</para>
    /// </summary>
    int OriginX => 0;

    /// <inheritdoc cref="OriginX"/>
    int OriginY => 0;

    /// <summary>
    /// Try to get the current screen into the capture's own reusable buffer. Returns true with a
    /// frame when pixels are available; returns false when nothing new was ready within the timeout
    /// (only the DXGI path reports this — it means the screen did not change). The returned frame
    /// borrows the capture's buffer and is valid only until the next SUCCESSFUL TryCapture call.
    ///
    /// Contract an implementation must honour: when this returns FALSE it must leave the buffer
    /// exactly as it was, so the last good frame is still readable. The frame loop relies on that to
    /// re-send tiles at a higher quality while the screen is standing still (see TileDiffer's
    /// CollectStale) — the moment nothing is changing is precisely the moment there are no fresh
    /// pixels to work from. Both implementations satisfy it today: DXGI returns before it copies
    /// anything, and GDI always returns true.
    /// </summary>
    bool TryCapture(int timeoutMilliseconds, out CapturedFrame frame);

    /// <summary>
    /// True when the machine cannot currently see its own screen at all — a locked desktop, a
    /// screensaver, a Windows security prompt, or a user switch. Distinct from TryCapture returning
    /// false, which normally just means the screen has not changed and is entirely healthy.
    ///
    /// Only the resilient wrapper can answer this, because it is the thing that catches the failure;
    /// a bare implementation throws instead, so the default here is "no news".
    /// </summary>
    bool ScreenUnavailable => false;
}

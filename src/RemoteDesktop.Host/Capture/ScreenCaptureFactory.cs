namespace RemoteDesktop.Host.Capture;

/// <summary>
/// Builds a screen capture, trying DXGI Desktop Duplication first and falling back to GDI BitBlt if
/// DXGI cannot start. The result is wrapped in <see cref="ResilientScreenCapture"/>, so a DXGI failure
/// mid-session also falls back to GDI rather than crashing.
/// </summary>
public static class ScreenCaptureFactory
{
    public static IScreenCapture Create(out string? dxgiFallbackReason, bool forceGdi = false, CaptureHealthLog? health = null)
    {
        dxgiFallbackReason = null;

        if (!forceGdi)
        {
            try
            {
                return new ResilientScreenCapture(new DxgiScreenCapture(health), health);
            }
            catch (Exception ex)
            {
                dxgiFallbackReason = ex.Message;
            }
        }
        else
        {
            dxgiFallbackReason = "forced to GDI for the DXGI-vs-GDI comparison";
        }

        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        return new ResilientScreenCapture(new GdiScreenCapture(bounds.Width, bounds.Height), health);
    }
}

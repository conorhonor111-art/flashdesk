namespace RemoteDesktop.Host.Capture;

/// <summary>
/// Builds a screen capture, trying DXGI Desktop Duplication first and falling back to GDI BitBlt if
/// DXGI cannot start (for example on a virtual machine without a real GPU). Reports which one won so
/// the window can display it.
/// </summary>
public static class ScreenCaptureFactory
{
    public static IScreenCapture Create(out string? dxgiFallbackReason)
    {
        dxgiFallbackReason = null;
        try
        {
            return new DxgiScreenCapture();
        }
        catch (Exception ex)
        {
            dxgiFallbackReason = ex.Message;
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                         ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            return new GdiScreenCapture(bounds.Width, bounds.Height);
        }
    }
}

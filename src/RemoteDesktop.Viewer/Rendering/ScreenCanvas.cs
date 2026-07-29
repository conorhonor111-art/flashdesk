using System.Drawing.Drawing2D;

namespace RemoteDesktop.Viewer.Rendering;

/// <summary>
/// Double-buffered display control. Draws the RemoteScreen bitmap scaled to fit the control while
/// preserving aspect ratio (letterboxed). Double buffering — drawing into one offscreen surface and
/// flipping it in a single step — removes the flicker a plain control shows when repainted many
/// times a second.
/// </summary>
public sealed class ScreenCanvas : Control
{
    private RemoteScreen? _screen;

    public ScreenCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
    }

    public void Bind(RemoteScreen screen)
    {
        _screen = screen;
        Invalidate();
    }

    /// <summary>Map a source tile rectangle to where it lands on screen and invalidate just that area.</summary>
    public void InvalidateTile(int tileX, int tileY, int tileW, int tileH)
    {
        if (_screen is null || _screen.Width == 0 || _screen.Height == 0) { Invalidate(); return; }

        var dest = DestinationRectangle();
        double scaleX = (double)dest.Width / _screen.Width;
        double scaleY = (double)dest.Height / _screen.Height;
        var area = new Rectangle(
            dest.X + (int)Math.Floor(tileX * scaleX),
            dest.Y + (int)Math.Floor(tileY * scaleY),
            (int)Math.Ceiling(tileW * scaleX) + 2,
            (int)Math.Ceiling(tileH * scaleY) + 2);
        Invalidate(area);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_screen is null || _screen.Width == 0)
        {
            e.Graphics.Clear(BackColor);
            return;
        }
        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
        _screen.DrawTo(e.Graphics, DestinationRectangle());
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    // Largest rectangle that fits the remote screen inside this control, keeping aspect ratio.
    private Rectangle DestinationRectangle()
    {
        if (_screen is null || _screen.Width == 0 || _screen.Height == 0)
            return ClientRectangle;

        double scale = Math.Min((double)Width / _screen.Width, (double)Height / _screen.Height);
        int w = Math.Max(1, (int)(_screen.Width * scale));
        int h = Math.Max(1, (int)(_screen.Height * scale));
        return new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
    }
}

using System.Drawing.Drawing2D;

namespace RemoteDesktop.Viewer.Rendering;

/// <summary>How the remote picture is sized in the window.</summary>
public enum DisplayMode
{
    /// <summary>Scale the whole remote screen to fit the window (aspect-preserving, letterboxed).</summary>
    Fit,

    /// <summary>Show real remote pixels 1:1, with scrollbars — no scaling, so text is exact.</summary>
    Actual,
}

/// <summary>
/// Double-buffered display control. In Fit mode it scales the RemoteScreen bitmap to the window with
/// high-quality interpolation; in Actual mode it draws 1:1 (its host panel supplies scrollbars).
/// Double buffering removes the flicker a plain control would show when repainted many times a second.
///
/// Scaling note: any time the window is smaller than the host resolution, Fit mode downscales, and
/// downscaling softens text no matter how high the JPEG quality is. Actual (1:1) mode is the way to
/// see true, unsoftened pixels.
/// </summary>
public sealed class ScreenCanvas : Control
{
    private RemoteScreen? _screen;

    public DisplayMode Mode { get; private set; } = DisplayMode.Fit;

    public ScreenCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        BackColor = Color.Black;
        Dock = DockStyle.Fill;
    }

    public void Bind(RemoteScreen screen)
    {
        _screen = screen;
        ApplyLayout();
        Invalidate();
    }

    public void SetMode(DisplayMode mode)
    {
        Mode = mode;
        ApplyLayout();
        Invalidate();
    }

    // Fit: fill the parent (which then shows no scrollbars). Actual: size to the exact remote
    // resolution so the AutoScroll parent shows scrollbars when it is bigger than the window.
    private void ApplyLayout()
    {
        if (Mode == DisplayMode.Fit)
        {
            Dock = DockStyle.Fill;
        }
        else
        {
            Dock = DockStyle.None;
            Location = new Point(0, 0);
            if (_screen is { Width: > 0, Height: > 0 })
                Size = new Size(_screen.Width, _screen.Height);
        }
    }

    /// <summary>Repaint just the area a changed tile occupies on screen.</summary>
    public void InvalidateTile(int tileX, int tileY, int tileW, int tileH)
    {
        if (_screen is null || _screen.Width == 0 || _screen.Height == 0) { Invalidate(); return; }

        if (Mode == DisplayMode.Actual)
        {
            Invalidate(new Rectangle(tileX, tileY, tileW, tileH));
            return;
        }

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

        if (Mode == DisplayMode.Actual)
        {
            // Exact pixels: no interpolation.
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            _screen.DrawTo(e.Graphics, new Rectangle(0, 0, _screen.Width, _screen.Height));
        }
        else
        {
            // Best available quality when scaling — matters most when downscaling text.
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _screen.DrawTo(e.Graphics, DestinationRectangle());
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Mode == DisplayMode.Fit) Invalidate();
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

using System.Drawing.Drawing2D;
using RemoteDesktop.UI;

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
/// high-quality interpolation; in Actual mode it draws 1:1 (its host panel supplies scrollbars). It
/// also draws the remote mouse pointer (so coordinate mapping can be judged by eye), maps window
/// clicks back to host pixels for input, and takes keyboard focus so keys can be captured.
/// </summary>
public sealed class ScreenCanvas : Control
{
    private RemoteScreen? _screen;

    private int _cursorX = -1;
    private int _cursorY = -1;
    private bool _cursorVisible;
    private Rectangle _cursorInvalidRect = Rectangle.Empty;

    // A simple arrow pointer, tip (hotspot) at (0,0).
    private static readonly Point[] ArrowShape =
    {
        new(0, 0), new(0, 16), new(4, 12), new(7, 17), new(9, 16), new(5, 11), new(11, 11),
    };

    public DisplayMode Mode { get; private set; } = DisplayMode.Fit;

    public ScreenCanvas()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Theme.CanvasBackdrop;
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

    // Take focus on click so keyboard events come here while controlling.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        base.OnMouseDown(e);
    }

    // Report every key (including arrows, Tab) as a KeyDown rather than letting WinForms consume it.
    protected override bool IsInputKey(Keys keyData) => true;

    // Fit: fill the parent (no scrollbars). Actual: size to the exact remote resolution so the
    // AutoScroll parent shows scrollbars when it is bigger than the window.
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

    /// <summary>Map a point in this control to host pixels. Returns false if it is outside the picture.</summary>
    public bool TryMapToHost(Point client, out int hostX, out int hostY)
    {
        hostX = hostY = 0;
        if (_screen is null || _screen.Width == 0 || _screen.Height == 0) return false;

        if (Mode == DisplayMode.Actual)
        {
            if (client.X < 0 || client.Y < 0 || client.X >= _screen.Width || client.Y >= _screen.Height) return false;
            hostX = client.X;
            hostY = client.Y;
            return true;
        }

        var dest = DestinationRectangle();
        if (!dest.Contains(client)) return false;
        hostX = Math.Clamp((int)((client.X - dest.X) * (long)_screen.Width / dest.Width), 0, _screen.Width - 1);
        hostY = Math.Clamp((int)((client.Y - dest.Y) * (long)_screen.Height / dest.Height), 0, _screen.Height - 1);
        return true;
    }

    /// <summary>Update where the host's pointer is (host pixels) and repaint just that area.</summary>
    public void SetRemoteCursor(int hostX, int hostY, bool visible)
    {
        _cursorX = hostX;
        _cursorY = hostY;
        _cursorVisible = visible;

        Rectangle newRect = Rectangle.Empty;
        if (visible && TryMapCursorToClient(out var p))
            newRect = new Rectangle(p.X - 1, p.Y - 1, 16, 22);

        if (newRect != _cursorInvalidRect)
        {
            if (!_cursorInvalidRect.IsEmpty) Invalidate(_cursorInvalidRect);
            if (!newRect.IsEmpty) Invalidate(newRect);
            _cursorInvalidRect = newRect;
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
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            _screen.DrawTo(e.Graphics, new Rectangle(0, 0, _screen.Width, _screen.Height));
        }
        else
        {
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _screen.DrawTo(e.Graphics, DestinationRectangle());
        }

        DrawRemoteCursor(e.Graphics);
    }

    private void DrawRemoteCursor(Graphics g)
    {
        if (!_cursorVisible || !TryMapCursorToClient(out var p)) return;

        var pts = new Point[ArrowShape.Length];
        for (int i = 0; i < pts.Length; i++)
            pts[i] = new Point(ArrowShape[i].X + p.X, ArrowShape[i].Y + p.Y);

        g.SmoothingMode = SmoothingMode.None;
        g.FillPolygon(Brushes.White, pts);
        g.DrawPolygon(Pens.Black, pts);
    }

    private bool TryMapCursorToClient(out Point p)
    {
        p = Point.Empty;
        if (_screen is null || _screen.Width == 0 || _screen.Height == 0 || _cursorX < 0 || _cursorY < 0) return false;

        if (Mode == DisplayMode.Actual)
        {
            p = new Point(_cursorX, _cursorY);
            return true;
        }

        var dest = DestinationRectangle();
        p = new Point(
            dest.X + (int)((long)_cursorX * dest.Width / _screen.Width),
            dest.Y + (int)((long)_cursorY * dest.Height / _screen.Height));
        return true;
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

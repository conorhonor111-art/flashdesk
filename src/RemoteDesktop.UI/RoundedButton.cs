using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A flat rounded-corner button painted entirely from Theme values: fill, border, text, and the
/// hover / pressed / disabled / keyboard-focus states. Painted with GraphicsPath + AntiAlias,
/// never Control.Region (Region clips without anti-aliasing and leaves jagged edges). The radius
/// is Theme.CornerRadius scaled for the monitor's DPI.
/// </summary>
public sealed class RoundedButton : Button
{
    private bool _hover;
    private bool _pressed;
    private ButtonKind _kind = ButtonKind.Neutral;

    public RoundedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public ButtonKind Kind
    {
        get => _kind;
        set
        {
            _kind = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        (Color fill, Color text, Color border) = Theme.ButtonColors(_kind, Enabled, _hover, _pressed);
        int radius = Theme.ScaledRadius(this);

        // Half-pixel inset so the 1 px anti-aliased border lands on whole pixels and stays crisp.
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using (var path = Theme.RoundedPath(bounds, radius))
        {
            // Gradient fill for Primary (blue) and Destructive (red) when enabled and in resting state.
            // All other states (disabled, pressed, neutral) keep the flat fill from Theme.ButtonColors.
            if (Enabled && !_pressed && (_kind == ButtonKind.Primary || _kind == ButtonKind.Destructive))
            {
                var gradTop = _kind == ButtonKind.Primary ? Theme.BlueGradientTop : Theme.RedGradientTop;
                var gradBot = _kind == ButtonKind.Primary ? Theme.BlueGradientBottom : Theme.RedGradientBottom;
                using var grad = new LinearGradientBrush(
                    new PointF(bounds.X, bounds.Y), new PointF(bounds.X, bounds.Bottom),
                    gradTop, gradBot);
                g.FillPath(grad, path);
            }
            else
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            using (var pen = new Pen(border, Theme.BorderThickness)) g.DrawPath(pen, path);
        }

        TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            float inset = Theme.BorderThickness + 2f;
            var inner = new RectangleF(bounds.X + inset, bounds.Y + inset,
                bounds.Width - inset * 2f, bounds.Height - inset * 2f);
            using var focusPath = Theme.RoundedPath(inner, Math.Max(1, radius - (int)inset));
            using var focusPen = new Pen(text, Theme.BorderThickness) { DashStyle = DashStyle.Dot };
            g.DrawPath(focusPen, focusPath);
        }
    }
}

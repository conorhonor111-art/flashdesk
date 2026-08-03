using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A checkbox drawn from <see cref="Theme"/> instead of the stock Windows glyph. These are not
/// decoration: on the operator window they are what turns remote control on and off, so they sit
/// beside safety-relevant words and should not look like they came from a different program.
///
/// ACCESSIBILITY: the state is carried by the CHECKMARK GLYPH, not by the blue fill. A checked box
/// stays distinguishable from an unchecked one in greyscale and to anyone who cannot separate the
/// two colours — the fill is reinforcement only. Never "simplify" this to a colour change.
/// </summary>
public sealed class ThemedCheckBox : CheckBox
{
    /// <summary>Box edge in logical pixels at 100% scaling.</summary>
    private const int BoxSize = 18;

    private bool _hover;
    private bool _pressed;

    public ThemedCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = true;
        Font = Theme.Body;
        ForeColor = Theme.TextPrimary;
        Cursor = Cursors.Hand;
    }

    private int Box => (int)Math.Round(BoxSize * DeviceDpi / 96.0);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        int box = Box;
        return new Size(box + Theme.S2 + text.Width + Theme.S1,
                        Math.Max(box, text.Height) + Theme.S1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int box = Box;
        int top = (Height - box) / 2;
        var bounds = new RectangleF(0.5f, top + 0.5f, box - 1f, box - 1f);

        // The single-radius rule is about surfaces the eye compares side by side. Theme's 12px
        // radius on an 18px box would round it into a circle, and a circle means "radio button" —
        // a different control with a different meaning. So the radius is clamped by the geometry,
        // which is not a second radius; it is the same one, as large as this shape can carry.
        float radius = Math.Min(Theme.ScaledRadius(this), box / 3f);
        using var path = Theme.RoundedPath(bounds, radius);

        Color fill, border;
        if (!Enabled)
        {
            fill = Checked ? Theme.DisabledBorder : Theme.DisabledFill;
            border = Theme.DisabledBorder;
        }
        else if (Checked)
        {
            fill = _pressed ? Theme.BluePressed : _hover ? Theme.BlueHover : Theme.Blue;
            border = fill;
        }
        else
        {
            fill = _pressed ? Theme.NeutralPressed : _hover ? Theme.NeutralHover : Theme.Card;
            border = _hover || _pressed ? Theme.BorderStrong : Theme.Border;
        }

        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(border, Theme.BorderThickness)) g.DrawPath(pen, path);

        if (Checked) DrawCheckmark(g, bounds, Enabled ? Theme.TextOnAccent : Theme.DisabledText);

        var textColour = Enabled ? ForeColor : Theme.DisabledText;
        var textRect = new Rectangle(box + Theme.S2, 0, Math.Max(0, Width - box - Theme.S2), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, textColour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            float inset = Theme.BorderThickness + 2f;
            var ring = new RectangleF(inset, inset, Width - inset * 2f, Height - inset * 2f);
            using var ringPath = Theme.RoundedPath(ring, Theme.ScaledRadius(this));
            using var ringPen = new Pen(textColour) { DashStyle = DashStyle.Dot };
            g.DrawPath(ringPen, ringPath);
        }
    }

    // Drawn as a stroke rather than a glyph so it stays crisp at any DPI and never depends on a
    // font having the character.
    private static void DrawCheckmark(Graphics g, RectangleF box, Color colour)
    {
        float w = box.Width, h = box.Height, x = box.X, y = box.Y;
        var points = new[]
        {
            new PointF(x + w * 0.24f, y + h * 0.52f),
            new PointF(x + w * 0.42f, y + h * 0.72f),
            new PointF(x + w * 0.76f, y + h * 0.29f),
        };
        using var pen = new Pen(colour, Math.Max(1.6f, w * 0.12f))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, points);
    }
}

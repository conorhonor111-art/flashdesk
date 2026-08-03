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

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

    private int Box => Scale(BoxSize);

    /// <summary>Gap between the box and its word.</summary>
    private int Gap => Scale(Theme.S2);

    /// <summary>
    /// Room reserved around everything for the keyboard-focus ring. Without it the box starts at
    /// x = 0 and the dotted ring is drawn straight through it.
    /// </summary>
    private int RingRoom => Scale(Theme.S1);

    /// <summary>The one radius, clamped by the shape it has to fit on — see the note in OnPaint.</summary>
    private float RadiusFor(float extent) => Math.Min(Theme.ScaledRadius(this), extent / 3f);

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _pressed = true; Invalidate(); } base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = TextRenderer.MeasureText(Text, Font);
        // The spacing is scaled like the box is, so the gap does not shrink relative to the
        // control at 125% and 150% display scaling.
        return new Size(RingRoom * 2 + Box + Gap + text.Width,
                        Math.Max(Box, text.Height) + RingRoom * 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int box = Box;
        int top = (Height - box) / 2;
        var bounds = new RectangleF(RingRoom + 0.5f, top + 0.5f, box - 1f, box - 1f);

        // The single-radius rule is about surfaces the eye compares side by side. Theme's 12px
        // radius on an 18px box would round it into a circle, and a circle means "radio button" —
        // a different control with a different meaning. So the radius is clamped by the geometry,
        // which is not a second radius; it is the same one, as large as this shape can carry.
        float radius = RadiusFor(box);
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
        int textLeft = RingRoom + box + Gap;
        var textRect = new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height);
        TextRenderer.DrawText(g, Text, Font, textRect, textColour,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        if (Focused && ShowFocusCues)
        {
            // Around the whole control — box and word together, inset 1px from the bounds. It was
            // previously inset 3px while the box started at x = 0, so the dotted ring was drawn
            // straight through the box. The box now starts at RingRoom, which puts the ring outside
            // it with a clean gap rather than shrinking the ring and moving the collision.
            var ring = new RectangleF(1f, 1f, Width - 2f, Height - 2f);
            using var ringPath = Theme.RoundedPath(ring, Theme.ScaledRadius(this));
            using var ringPen = new Pen(textColour, Theme.BorderThickness) { DashStyle = DashStyle.Dot };
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

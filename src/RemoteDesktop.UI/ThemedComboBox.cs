using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A dropdown whose item rows, border and chevron are drawn from <see cref="Theme"/> rather than in
/// stock Win32 chrome.
///
/// ⚠️ THE CHROME CANNOT BE DRAWN FROM OnPaint, however natural that looks. ComboBox wraps a native
/// Win32 control and never raises OnPaint, so an override there compiles, reads correctly, and
/// silently does nothing. That is not a hypothetical: this control previously drew its chevron from
/// OnPaint and therefore showed the SYSTEM arrow the whole time, while the code and the commit
/// message both said otherwise. MEASURED, not read — a probe counting calls on a ComboBox subclass
/// recorded OnPaint 0 and OnDrawItem 8, while a plain Panel in the same window recorded Paint 7.
/// So the chrome is painted from WndProc, after the control has finished its own WM_PAINT (and
/// WM_PRINTCLIENT, so it survives a DWM or thumbnail redraw).
///
/// HONEST LIMIT, so nobody later thinks it was forgotten: the POPUP LIST is created and drawn by
/// Windows. Owner-draw reaches the item rows inside it, which is why those follow Theme, but the
/// popup's own frame and shadow are not reachable from managed code. Replacing it would mean
/// building a custom popup window — a great deal of new surface for a control that appears only in
/// the technical view. The closed box, its border, the item rows and the chevron are ours; the
/// popup frame is not.
/// </summary>
public sealed class ThemedComboBox : ComboBox
{
    private const int WmPaint = 0x000F;
    private const int WmPrintClient = 0x0318;

    private bool _hover;

    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;          // removes the sunken 3D Win32 edge
        DropDownStyle = ComboBoxStyle.DropDownList;
        Font = Theme.Body;
        BackColor = Theme.Card;
        ForeColor = Theme.TextPrimary;
        Cursor = Cursors.Hand;
    }

    // The border colour depends on all of these, and WndProc only repaints when Windows decides to,
    // so each state change has to ask for one.
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnDropDown(EventArgs e) { Invalidate(); base.OnDropDown(e); }
    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;

        var g = e.Graphics;
        bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        // The closed box reuses this same path (ComboBoxEdit passes the selected item through), so
        // the highlight must only apply inside the open list — otherwise the resting control would
        // sit permanently filled blue.
        bool inList = (e.State & DrawItemState.ComboBoxEdit) != DrawItemState.ComboBoxEdit;

        Color fill = !Enabled ? Theme.DisabledFill
                   : selected && inList ? Theme.Blue
                   : Theme.Card;
        Color text = !Enabled ? Theme.DisabledText
                   : selected && inList ? Theme.TextOnAccent
                   : Theme.TextPrimary;

        using (var brush = new SolidBrush(fill)) g.FillRectangle(brush, e.Bounds);

        var label = Items[e.Index]?.ToString() ?? string.Empty;
        var textRect = Rectangle.Inflate(e.Bounds, -Theme.S1, 0);
        TextRenderer.DrawText(g, label, Font, textRect, text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        base.OnDrawItem(e);
    }

    // Painted after the control has drawn itself.
    //
    // ⚠️ The two cases must NOT share a device context. WM_PAINT draws to the window, but
    // WM_PRINTCLIENT hands us the target HDC in wParam, and painting a WM_PRINTCLIENT with
    // CreateGraphics would draw to the screen while the thumbnail kept the system chrome — the same
    // class of silent no-op as the original OnPaint override, just harder to notice.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (!IsHandleCreated) return;

        if (m.Msg == WmPaint)
        {
            using var g = CreateGraphics();
            PaintChrome(g);
        }
        else if (m.Msg == WmPrintClient && m.WParam != IntPtr.Zero)
        {
            using var g = Graphics.FromHdc(m.WParam);
            PaintChrome(g);
        }
    }

    /// <summary>
    /// Covers what Windows drew and puts our own frame and chevron in its place, onto whichever
    /// surface the caller is painting. The item text is left alone — it is already correct from
    /// OnDrawItem, and a wider cover would eat it.
    /// </summary>
    private void PaintChrome(Graphics g)
    {
        int edge = Scale(2);
        int button = Scale(Theme.S4);
        if (Width <= button + edge || Height <= edge * 2) return;

        g.SmoothingMode = SmoothingMode.AntiAlias;

        Color fill = Enabled ? Theme.Card : Theme.DisabledFill;
        Color glyph = Enabled ? Theme.TextSecondary : Theme.DisabledText;
        Color border = !Enabled ? Theme.DisabledBorder
                     : Focused || DroppedDown ? Theme.Blue
                     : _hover ? Theme.BorderStrong
                     : Theme.Border;

        int radius = Theme.ScaledRadius(this);
        var client = new Rectangle(0, 0, Width, Height);
        using var outline = Theme.RoundedPath(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), radius);

        // Outside the rounded outline, show whatever is behind the control. This is the same
        // clear-to-parent trick CardPanel and RoundedButton use, and it is what makes the corners
        // read as round rather than as pale notches cut out of a square.
        using (var outside = new Region(client))
        {
            outside.Exclude(outline);
            using var brush = new SolidBrush(Parent?.BackColor ?? Theme.Window);
            g.FillRegion(brush, outside);
        }

        // Two things Windows drew are not ours: the flat system frame band, and the square the
        // system arrow sits in. Cover both, clipped to the rounded outline.
        using (var clip = new Region(outline))
        using (var brush = new SolidBrush(fill))
        {
            g.Clip = clip;
            using (var frame = new Region(client))
            {
                frame.Exclude(Rectangle.Inflate(client, -edge, -edge));
                g.FillRegion(brush, frame);
            }
            g.FillRectangle(brush, new Rectangle(Width - button, edge, button - edge, Height - edge * 2));
            g.ResetClip();
        }

        float arm = Scale(Theme.S1);
        float cx = Width - button / 2f;
        float cy = Height / 2f;
        var chevron = new[]
        {
            new PointF(cx - arm, cy - arm * 0.45f),
            new PointF(cx, cy + arm * 0.55f),
            new PointF(cx + arm, cy - arm * 0.45f),
        };
        using (var pen = new Pen(glyph, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        })
        {
            g.DrawLines(pen, chevron);
        }

        // A thicker focus border straddles the outline, so its path is inset by half the thickness
        // to keep the whole stroke inside the control.
        float thickness = (Focused || DroppedDown) && Enabled ? 2f : Theme.BorderThickness;
        using (var borderPath = Theme.RoundedPath(
            new RectangleF(thickness / 2f, thickness / 2f, Width - thickness, Height - thickness), radius))
        using (var pen = new Pen(border, thickness))
        {
            g.DrawPath(pen, borderPath);
        }
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A dropdown whose item rows and chevron are drawn from <see cref="Theme"/> rather than in stock
/// Win32 chrome.
///
/// ⚠️ THE CHEVRON CANNOT BE DRAWN FROM OnPaint, however natural that looks. ComboBox wraps a
/// native Win32 control and never raises OnPaint, so an override there compiles, reads correctly,
/// and silently does nothing. That is not a hypothetical: this control previously drew its chevron
/// from OnPaint and therefore showed the SYSTEM arrow the whole time, while the code and the commit
/// message both said otherwise. MEASURED, not read — a probe counting calls on a ComboBox subclass
/// recorded OnPaint 0 and OnDrawItem 8, while a plain Panel in the same window recorded Paint 7.
/// So the chevron is painted from WndProc, after the control has finished its own WM_PAINT (and
/// WM_PRINTCLIENT, so it survives a DWM or thumbnail redraw).
///
/// HONEST LIMITS, so nobody later thinks these were forgotten or "fixes" them by accident:
///  - The POPUP LIST is created and drawn by Windows. Owner-draw reaches the item rows inside it,
///    which is why those follow Theme, but the popup's own frame and shadow are not reachable from
///    managed code. Replacing it would mean building a custom popup window — a great deal of new
///    surface for a control that appears only in the technical view.
///  - The closed box's BORDER is still the FlatStyle.Flat system border in a system grey, and its
///    corners are square, not rounded like every other surface in the design system.
/// The item rows, the item text and the chevron are ours. The popup frame and the closed box's
/// border are not.
/// </summary>
public sealed class ThemedComboBox : ComboBox
{
    private const int WmPaint = 0x000F;
    private const int WmPrintClient = 0x0318;

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

    // Painted after the control has drawn itself. WM_PRINTCLIENT is handled as well as WM_PAINT so
    // the chevron survives a redraw that does not go through the normal paint path — a DWM
    // thumbnail or a PrintWindow capture.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WmPaint || m.Msg == WmPrintClient) PaintChevron();
    }

    /// <summary>
    /// Covers the system arrow and draws ours in its place. Only the arrow's own square is
    /// repainted: the item text is already in place from OnDrawItem, and a wider rectangle would
    /// eat it.
    /// </summary>
    private void PaintChevron()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0) return;

        ChevronPaintCount++;

        using var g = CreateGraphics();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // The cover must come FIRST — without it the system arrow shows through around ours.
        int edge = Scale(2);
        int buttonWidth = Scale(Theme.S4);
        if (Width <= buttonWidth + edge) return;
        using (var cover = new SolidBrush(Enabled ? Theme.Card : Theme.DisabledFill))
            g.FillRectangle(cover, new Rectangle(
                Width - buttonWidth, edge, buttonWidth - edge, Height - edge * 2));

        float size = 4f * DeviceDpi / 96f;
        float cx = Width - (12f * DeviceDpi / 96f);
        float cy = Height / 2f;
        var chevron = new[]
        {
            new PointF(cx - size, cy - size * 0.45f),
            new PointF(cx, cy + size * 0.55f),
            new PointF(cx + size, cy - size * 0.45f),
        };

        using var pen = new Pen(Enabled ? Theme.TextSecondary : Theme.DisabledText, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(pen, chevron);
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
}

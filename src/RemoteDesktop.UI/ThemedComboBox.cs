using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A dropdown drawn from <see cref="Theme"/> rather than in stock Win32 chrome.
///
/// HONEST LIMIT, so nobody later thinks this was forgotten: the POPUP LIST that appears when the
/// box is open is created and drawn by Windows itself. Owner-draw reaches the items inside it —
/// which is why the list rows do follow Theme — but the popup's own frame and shadow are not
/// reachable from managed code. Making that rounded would mean replacing the control with a
/// custom popup window, which is a great deal of new surface for a control that appears only in
/// the technical view. The closed box, the text and the chevron are ours; the popup frame is not.
/// </summary>
public sealed class ThemedComboBox : ComboBox
{
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

    // FlatStyle.Flat still paints a system chevron in the system colour. Drawing our own on top
    // keeps every mark on the control a Theme value.
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

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
}

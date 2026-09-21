using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A white rounded-corner card with a hairline border — the grouping surface of the design
/// system. Painted with GraphicsPath + AntiAlias, never Control.Region. Children inherit the
/// card's background colour; keep the card's Padding at or above the corner radius so no child
/// paints over a rounded corner.
/// </summary>
public sealed class CardPanel : Panel
{
    public CardPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.Card;
        Padding = new Padding(Theme.S3);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;

        // The corners outside the rounded outline must show the window surface behind the card.
        using (var outside = new SolidBrush(Parent?.BackColor ?? Theme.Window))
            g.FillRectangle(outside, ClientRectangle);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        using var path = Theme.RoundedPath(bounds, Theme.ScaledRadius(this));
        using (var fill = new SolidBrush(Theme.Card)) g.FillPath(fill, path);
        using (var pen = new Pen(Theme.Border, Theme.BorderThickness)) g.DrawPath(pen, path);
        // Elevation shadow: a slightly darker hairline along bottom and right edges only.
        // Gives cards a soft "sitting on the surface" depth without composited alpha.
        var elevR = new RectangleF(bounds.X + 1f, bounds.Y + 1f, bounds.Width - 2f, bounds.Height - 2f);
        using var elevPen = new Pen(Theme.CardElevBorder, 0.75f);
        g.DrawLine(elevPen, elevR.X + 2f, elevR.Bottom, elevR.Right - 2f, elevR.Bottom);
        g.DrawLine(elevPen, elevR.Right, elevR.Y + 2f, elevR.Right, elevR.Bottom - 2f);
    }
}

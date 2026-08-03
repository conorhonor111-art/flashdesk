using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>
/// A text field that belongs to the same design system as the buttons and cards. WinForms' own
/// TextBox draws a sunken square Win32 border that reads as a different era — and the peer-number
/// field sits directly beside a rounded button, which is where that mismatch is most visible.
///
/// So the border is painted here and a real, borderless TextBox lives inside. The inner control
/// still does all the text work — caret, selection, IME, clipboard, undo — none of which is worth
/// reimplementing for a rounded outline.
///
/// Focus is shown by a 2px <see cref="Theme.Blue"/> border. That is the sanctioned meaning of blue
/// ("a decision being asked right now"), and the field you are typing into is exactly that.
/// </summary>
public sealed class RoundedTextBox : UserControl
{
    private readonly TextBox _inner = new() { BorderStyle = BorderStyle.None };
    private bool _hover;

    public RoundedTextBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        Padding = new Padding(Theme.S2, Theme.S1, Theme.S2, Theme.S1);
        Font = Theme.Body;

        _inner.Font = Theme.Body;
        _inner.BackColor = Theme.Card;
        _inner.ForeColor = Theme.TextPrimary;
        _inner.GotFocus += (_, _) => Invalidate();
        _inner.LostFocus += (_, _) => Invalidate();
        _inner.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _inner.MouseEnter += (_, _) => SetHover(true);
        _inner.MouseLeave += (_, _) => SetHover(false);
        // Key events land on the inner TextBox, so they are re-raised here — otherwise a caller
        // subscribing to this control's KeyDown (the Enter-to-connect shortcut) would never fire.
        _inner.KeyDown += (_, e) => OnKeyDown(e);
        _inner.KeyPress += (_, e) => OnKeyPress(e);
        Controls.Add(_inner);

        Height = PreferredHeight;
    }

    /// <summary>The inner control, for the few cases that need the real TextBox (e.g. Select).</summary>
    public TextBox Inner => _inner;

    // AllowNull matches Control.Text, whose setter accepts null and stores it as "".
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => _inner.Text;
        set => _inner.Text = value ?? string.Empty;
    }

    public int MaxLength
    {
        get => _inner.MaxLength;
        set => _inner.MaxLength = value;
    }

    public HorizontalAlignment TextAlign
    {
        get => _inner.TextAlign;
        set => _inner.TextAlign = value;
    }

    public string PlaceholderText
    {
        get => _inner.PlaceholderText;
        set => _inner.PlaceholderText = value;
    }

    public bool ReadOnly
    {
        get => _inner.ReadOnly;
        set => _inner.ReadOnly = value;
    }

    public int SelectionStart
    {
        get => _inner.SelectionStart;
        set => _inner.SelectionStart = value;
    }

    public int SelectionLength
    {
        get => _inner.SelectionLength;
        set => _inner.SelectionLength = value;
    }

    /// <summary>Tall enough for the inner text plus the padding, so callers never guess a height.</summary>
    public int PreferredHeight => _inner.PreferredHeight + Padding.Vertical;

    private void SetHover(bool hover)
    {
        if (_hover == hover) return;
        _hover = hover;
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e) { SetHover(true); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { SetHover(false); base.OnMouseLeave(e); }

    /// <summary>Clicking anywhere in the rounded area puts the caret in the text, not just the text itself.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _inner.Focus();
        base.OnMouseDown(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        _inner.Focus();
        base.OnGotFocus(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        _inner.Enabled = Enabled;
        _inner.BackColor = Enabled ? Theme.Card : Theme.DisabledFill;
        _inner.ForeColor = Enabled ? Theme.TextPrimary : Theme.DisabledText;
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _inner.Font = Font;
        Height = PreferredHeight;
    }

    // Single-line TextBox refuses to be stretched, so it is centred by hand rather than docked —
    // docking would pin the text to the top of the rounded box and look like a rendering bug.
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int innerHeight = _inner.PreferredHeight;
        _inner.SetBounds(Padding.Left, (Height - innerHeight) / 2,
                         Math.Max(0, Width - Padding.Horizontal), innerHeight);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // Clear to the parent surface first so the area outside the rounded outline shows the card
        // behind it rather than a square of fill — the same trick CardPanel and RoundedButton use.
        g.Clear(Parent?.BackColor ?? Theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        bool focused = _inner.Focused;
        float thickness = focused ? 2f : Theme.BorderThickness;
        float inset = thickness / 2f;
        var bounds = new RectangleF(inset, inset, Width - thickness, Height - thickness);
        using var path = Theme.RoundedPath(bounds, Theme.ScaledRadius(this));

        Color fill = Enabled ? Theme.Card : Theme.DisabledFill;
        Color border = !Enabled ? Theme.DisabledBorder
                     : focused ? Theme.Blue
                     : _hover ? Theme.BorderStrong
                     : Theme.Border;

        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(border, thickness)) g.DrawPath(pen, path);
    }
}

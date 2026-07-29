using System.Drawing;
using System.Windows.Forms;

namespace RemoteDesktop.UI;

/// <summary>How a button reads semantically — which decides its colour.</summary>
public enum ButtonKind
{
    Neutral,
    Primary,      // the main action being offered (blue)
    Destructive,  // disconnect / reject / revoke — ending only (red)
}

/// <summary>
/// The one design system for every window (host, viewer, and the Stage 4 dialog + indicator strip).
/// Colours, fonts and spacing are defined once here so none is ever invented ad-hoc. See the
/// "Design system" section of CLAUDE.md for the meanings and the reasoning — especially why the
/// live-session indicator is AMBER and never green.
/// </summary>
public static class Theme
{
    // Neutral (structure / no state)
    public static readonly Color Window = Color.FromArgb(0xF5, 0xF6, 0xF8);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(0xC6, 0xCC, 0xD4);
    public static readonly Color TextPrimary = Color.FromArgb(0x1B, 0x1F, 0x24);
    public static readonly Color TextSecondary = Color.FromArgb(0x5A, 0x62, 0x6C);
    public static readonly Color TextOnAccent = Color.White;

    // Operator (viewer) side — a graphite header so my side never looks like the client side
    public static readonly Color OperatorHeader = Color.FromArgb(0x24, 0x29, 0x31);
    public static readonly Color OperatorHeaderText = Color.FromArgb(0xEC, 0xEF, 0xF3);

    // Semantic accents — one colour, one meaning, used nowhere else
    public static readonly Color Green = Color.FromArgb(0x1E, 0x7E, 0x34); // ready / running / nobody connected
    public static readonly Color Blue = Color.FromArgb(0x1A, 0x73, 0xE8);  // a decision being asked right now
    public static readonly Color Amber = Color.FromArgb(0xB2, 0x6A, 0x00); // a session is LIVE, someone is watching
    public static readonly Color AmberFill = Color.FromArgb(0xF4, 0xB4, 0x00);
    public static readonly Color Red = Color.FromArgb(0xC5, 0x22, 0x1F);   // disconnect / reject / revoke — ending only

    // Type scale — Segoe UI, four sizes, two weights (regular + semibold)
    public static readonly Font Display = new("Segoe UI", 24f, FontStyle.Regular);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f, FontStyle.Regular);
    public static readonly Font Body = new("Segoe UI", 10f, FontStyle.Regular);
    public static readonly Font Small = new("Segoe UI", 9f, FontStyle.Regular);

    // Spacing scale (px at 100%) — use these, never ad-hoc numbers
    public const int S1 = 4;
    public const int S2 = 8;
    public const int S3 = 16;
    public const int S4 = 24;
    public const int S5 = 32;

    /// <summary>Base window styling: neutral surface, system font, DPI-aware font scaling.</summary>
    public static void ApplyWindow(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = TextPrimary;
        form.Font = Body;
        form.AutoScaleMode = AutoScaleMode.Font;
    }

    public static Button MakeButton(string text, ButtonKind kind = ButtonKind.Neutral)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            Font = Body,
            Padding = new Padding(S3, S2, S3, S2),
            Margin = new Padding(S2),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        Style(button, kind);
        return button;
    }

    public static void Style(Button button, ButtonKind kind)
    {
        (Color fill, Color text, Color border) = kind switch
        {
            ButtonKind.Primary => (Blue, TextOnAccent, Blue),
            ButtonKind.Destructive => (Red, TextOnAccent, Red),
            _ => (Card, TextPrimary, Border),
        };
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = fill;
        button.ForeColor = text;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = 1;
    }

    public static Label Caption(string text) => new()
    {
        Text = text,
        Font = Small,
        ForeColor = TextSecondary,
        AutoSize = true,
        Margin = new Padding(0, S1, 0, 0),
    };

    public static Label HeadingLabel(string text) => new()
    {
        Text = text,
        Font = Heading,
        ForeColor = TextPrimary,
        AutoSize = true,
        Margin = new Padding(0),
    };
}

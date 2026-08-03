using System.Drawing;
using System.Drawing.Drawing2D;
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
/// Colours, fonts, spacing, corner radius and window sizes are defined once here so none is ever
/// invented ad-hoc. See the "Design system" section of CLAUDE.md for the meanings and the
/// reasoning — especially why the live-session indicator is AMBER and never green.
/// </summary>
public static class Theme
{
    // Neutral (structure / no state)
    public static readonly Color Window = Color.FromArgb(0xF5, 0xF6, 0xF8);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(0xC6, 0xCC, 0xD4);
    public static readonly Color BorderStrong = Color.FromArgb(0xA9, 0xB1, 0xBC); // neutral border under the pointer
    public static readonly Color TextPrimary = Color.FromArgb(0x1B, 0x1F, 0x24);
    // Nudged hue-neutral 2026-07-30 (was 5A626C): at Small sizes ClearType fringing gave the old
    // blue-grey a greenish cast that made secondary text read as a state colour. Contrast on
    // white stays >= 4.5:1.
    public static readonly Color TextSecondary = Color.FromArgb(0x60, 0x62, 0x66);
    public static readonly Color TextOnAccent = Color.White;

    // Operator (viewer) side — a graphite header so my side never looks like the client side
    public static readonly Color OperatorHeader = Color.FromArgb(0x24, 0x29, 0x31);
    public static readonly Color OperatorHeaderText = Color.FromArgb(0xEC, 0xEF, 0xF3);

    // Semantic accents — one colour, one meaning, used nowhere else. Green LEFT this set on
    // 2026-07-30: idle is signalled by a neutral grey dot + word (idle should not attract the
    // eye), which freed green to become the brand colour below.
    public static readonly Color Blue = Color.FromArgb(0x1A, 0x73, 0xE8);  // a decision being asked right now
    public static readonly Color Amber = Color.FromArgb(0xB2, 0x6A, 0x00); // a session is LIVE, someone is watching
    public static readonly Color AmberFill = Color.FromArgb(0xF4, 0xB4, 0x00);
    public static readonly Color Red = Color.FromArgb(0xC5, 0x22, 0x1F);   // disconnect / reject / revoke — ending only

    // Brand — lives ONLY in the mark (icon, and later lockup surfaces). Never a state signal.
    // PALETTE LOCK with the download site (site/index.html): the site's background is BrandTile,
    // its one clickable green is BrandGreen, its text is OperatorHeaderText. If any of these
    // three values changes here, site/index.html must be updated in the same commit — the two
    // must never drift (Conor, 2026-07-30). assets\make-icon.ps1 mirrors these values too.
    public static readonly Color BrandGreen = Color.FromArgb(0x2B, 0xD1, 0x6B);
    public static readonly Color BrandTile = Color.FromArgb(0x17, 0x19, 0x1E);

    // Interaction shades — the sanctioned hover/pressed step of each accent, one step darker so the
    // change is visible but the meaning (the accent colour) stays the same. Never derive ad-hoc.
    public static readonly Color NeutralHover = Color.FromArgb(0xF0, 0xF2, 0xF5);
    public static readonly Color NeutralPressed = Color.FromArgb(0xE4, 0xE8, 0xED);
    public static readonly Color BlueHover = Color.FromArgb(0x16, 0x67, 0xD0);
    public static readonly Color BluePressed = Color.FromArgb(0x12, 0x5B, 0xB5);
    public static readonly Color RedHover = Color.FromArgb(0xA8, 0x1D, 0x1B);
    public static readonly Color RedPressed = Color.FromArgb(0x8F, 0x18, 0x15);

    // Disabled controls — visibly inert, still readable
    public static readonly Color DisabledFill = Color.FromArgb(0xED, 0xEF, 0xF2);
    public static readonly Color DisabledText = Color.FromArgb(0x8A, 0x91, 0x9B);
    public static readonly Color DisabledBorder = Color.FromArgb(0xD5, 0xDA, 0xE0);

    // Type scale — Segoe UI, four sizes + one sanctioned exception, two weights (regular + semibold)
    public static readonly Font Display = new("Segoe UI", 24f, FontStyle.Regular);
    public static readonly Font Heading = new("Segoe UI Semibold", 12f, FontStyle.Regular);
    public static readonly Font Body = new("Segoe UI", 10f, FontStyle.Regular);
    public static readonly Font Small = new("Segoe UI", 9f, FontStyle.Regular);

    /// <summary>
    /// Reserved EXCLUSIVELY for the connection code/address hero. The code is the product's
    /// most-seen artifact — a number read aloud over a phone by a stressed person — so it gets the
    /// one size outside the four-step scale (decided 2026-07-30). Do not use it for anything else.
    /// </summary>
    public static readonly Font Hero = new("Segoe UI Semibold", 36f, FontStyle.Regular);

    // Spacing scale (px at 100%) — use these, never ad-hoc numbers
    public const int S1 = 4;
    public const int S2 = 8;
    public const int S3 = 16;
    public const int S4 = 24;
    public const int S5 = 32;

    /// <summary>
    /// Corner radius in logical pixels at 100% scaling — THE one radius, used everywhere (two
    /// radii in one window looks like an accident). Raised 8 → 12 on 2026-07-30: 8 read as a
    /// timidly softened rectangle rather than a decision. Rounded shapes are painted with
    /// GraphicsPath + AntiAlias, never Control.Region — Region clips without anti-aliasing and
    /// leaves jagged edges.
    /// </summary>
    public const int CornerRadius = 12;

    /// <summary>Hairline border thickness for cards and buttons.</summary>
    public const float BorderThickness = 1f;

    /// <summary>The corner radius scaled for the monitor the control is actually on.</summary>
    public static int ScaledRadius(Control control) =>
        (int)Math.Round(CornerRadius * control.DeviceDpi / 96.0);

    // Deliberate window sizes — design decisions, so they live here and not in the forms.
    // The host window is sized to its content in each of its two views (no dead space).
    // Tall enough for the worst case in the simple view: "No number yet" plus a two-line
    // explanation. In the normal state that shows as a little extra breathing room, which is
    // the right trade — a clipped instruction is a phone call.
    public static readonly Size HostSimpleSize = new(500, 470);     // client area, simple view
    public static readonly Size HostTechnicalSize = new(500, 800);  // client area, technical open
    public static readonly Size HostWindowMinimum = new(460, 450);  // outer bounds
    public static readonly Size ViewerWindowSize = new(1000, 660);  // client area — room for a scaled 1080p picture
    public static readonly Size ViewerWindowMinimum = new(640, 480);

    /// <summary>The letterbox behind the remote picture. Pure black on purpose — it makes the
    /// remote screen's edges unmistakable and never competes with its content.</summary>
    public static readonly Color CanvasBackdrop = Color.Black;

    /// <summary>Height of the full-width status band at the top of the host window — the band
    /// blends with the window when idle and turns solid amber while a session is live.</summary>
    public const int StatusBandHeight = 56;
    public const int SmallFieldWidth = 72;                          // e.g. the quality dropdown
    public const int MediumFieldWidth = 150;                        // e.g. the viewer's address box

    /// <summary>
    /// Base window styling: neutral surface, system font, DPI-aware font scaling, and — on
    /// Windows 11 — an OS-rounded outer frame (Windows 10 refuses the call and keeps the square
    /// frame; that is the intended clean degrade).
    /// </summary>
    public static void ApplyWindow(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = TextPrimary;
        form.Font = Body;
        form.AutoScaleMode = AutoScaleMode.Font;
        form.HandleCreated += (_, _) => WindowCorners.Apply(form);
    }

    /// <summary>
    /// A small, quiet chip-sized button for secondary actions that must never compete with what
    /// they sit beside (e.g. Copy next to the hero address — the number has to win the glance).
    /// </summary>
    public static Button MakeQuietButton(string text, ButtonKind kind = ButtonKind.Neutral)
    {
        var button = new RoundedButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = Small,
            Padding = new Padding(S2, S1, S2, S1),
            Margin = new Padding(S2),
            Cursor = Cursors.Hand,
        };
        Style(button, kind);
        return button;
    }

    public static Button MakeButton(string text, ButtonKind kind = ButtonKind.Neutral)
    {
        var button = new RoundedButton
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = Body,
            Padding = new Padding(S3, S2, S3, S2),
            Margin = new Padding(S2),
            Cursor = Cursors.Hand,
        };
        Style(button, kind);
        return button;
    }

    public static void Style(Button button, ButtonKind kind)
    {
        if (button is RoundedButton rounded)
        {
            rounded.Kind = kind;
            return;
        }

        // Legacy flat styling for a plain WinForms Button (none remain in the themed windows,
        // kept so an unconverted call site still gets sane colours instead of system chrome).
        (Color fill, Color text, Color border) = ButtonColors(kind, enabled: true, hover: false, pressed: false);
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = fill;
        button.ForeColor = text;
        button.FlatAppearance.BorderColor = border;
        button.FlatAppearance.BorderSize = (int)BorderThickness;
    }

    /// <summary>
    /// State-resolved colours for a themed button, kept here so every interactive state colour is
    /// a Theme value and none is ever typed into a control.
    /// </summary>
    public static (Color Fill, Color Text, Color Border) ButtonColors(ButtonKind kind, bool enabled, bool hover, bool pressed)
    {
        if (!enabled) return (DisabledFill, DisabledText, DisabledBorder);
        return kind switch
        {
            ButtonKind.Primary => (
                pressed ? BluePressed : hover ? BlueHover : Blue,
                TextOnAccent,
                pressed ? BluePressed : hover ? BlueHover : Blue),
            ButtonKind.Destructive => (
                pressed ? RedPressed : hover ? RedHover : Red,
                TextOnAccent,
                pressed ? RedPressed : hover ? RedHover : Red),
            _ => (
                pressed ? NeutralPressed : hover ? NeutralHover : Card,
                TextPrimary,
                hover || pressed ? BorderStrong : Border),
        };
    }

    /// <summary>A rounded-rectangle outline for painting with anti-aliasing.</summary>
    public static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
        if (d <= 0f)
        {
            path.AddRectangle(bounds);
            return path;
        }
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// A quiet text link — for secondary navigation that must never compete with the one real
    /// action (e.g. "Technical details" on the host window).
    /// </summary>
    public static LinkLabel MakeLink(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Small,
        LinkColor = TextSecondary,
        ActiveLinkColor = TextPrimary,
        VisitedLinkColor = TextSecondary,
        LinkBehavior = LinkBehavior.AlwaysUnderline,
        Margin = new Padding(0),
    };

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

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
    // =========================================================================
    // COLOURS — neutral (structure / no state)
    // =========================================================================

    /// <summary>Main window background surface. #F5F6F8.</summary>
    public static readonly Color Window = Color.FromArgb(0xF5, 0xF6, 0xF8);

    /// <summary>Canonical alias for <see cref="Window"/> (spec name: WindowBackground).</summary>
    public static readonly Color WindowBackground = Window;

    /// <summary>Card / elevated-surface background. Pure white.</summary>
    public static readonly Color Card = Color.White;

    /// <summary>Canonical alias for <see cref="Card"/> (spec name: CardBackground).</summary>
    public static readonly Color CardBackground = Card;

    /// <summary>Default hairline border between surfaces. #C6CCD4.</summary>
    public static readonly Color Border = Color.FromArgb(0xC6, 0xCC, 0xD4);

    /// <summary>Canonical alias for <see cref="Border"/> (spec name: BorderColor).</summary>
    public static readonly Color BorderColor = Border;

    /// <summary>Neutral border when a control is hovered or focused. #A9B1BC.</summary>
    public static readonly Color BorderStrong = Color.FromArgb(0xA9, 0xB1, 0xBC);

    /// <summary>Primary readable text. #1B1F24.</summary>
    public static readonly Color TextPrimary = Color.FromArgb(0x1B, 0x1F, 0x24);

    /// <summary>
    /// Secondary / muted text. #606266 — nudged hue-neutral 2026-07-30 (was 5A626C): at Small
    /// sizes ClearType fringing gave the old blue-grey a greenish cast that made secondary text
    /// read as a state colour. Contrast on white stays &gt;= 4.5:1.
    /// </summary>
    public static readonly Color TextSecondary = Color.FromArgb(0x60, 0x62, 0x66);

    /// <summary>Text placed on any solid-colour accent fill (blue, red, amber). White.</summary>
    public static readonly Color TextOnAccent = Color.White;

    // =========================================================================
    // COLOURS — operator (viewer) side
    // =========================================================================

    /// <summary>
    /// Graphite header bar on the operator (viewer) window. #242931 — visually distinct from
    /// the host window so the operator side never looks like the client side.
    /// </summary>
    public static readonly Color OperatorHeader = Color.FromArgb(0x24, 0x29, 0x31);

    /// <summary>Text and icon colour on the operator header bar. #ECEFF3.</summary>
    public static readonly Color OperatorHeaderText = Color.FromArgb(0xEC, 0xEF, 0xF3);

    /// <summary>Canonical alias for <see cref="OperatorHeaderText"/> (spec name: OperatorText).</summary>
    public static readonly Color OperatorText = OperatorHeaderText;

    // =========================================================================
    // COLOURS — semantic accents (one colour, one meaning, used nowhere else)
    // =========================================================================

    /// <summary>
    /// Decision blue — a decision is being asked RIGHT NOW. #1A73E8.
    /// Used only on Primary buttons and the one CTA that closes a decision.
    /// Green LEFT this set on 2026-07-30: idle is now signalled by a neutral grey dot + word
    /// (idle must not attract the eye), which freed green to become the brand colour.
    /// </summary>
    public static readonly Color Blue = Color.FromArgb(0x1A, 0x73, 0xE8);

    /// <summary>Canonical alias for <see cref="Blue"/> (spec name: BlueDecision).</summary>
    public static readonly Color BlueDecision = Blue;

    /// <summary>
    /// Amber text on the live-state band. #B26A00 — a session is LIVE, someone is watching.
    /// THIS IS A SAFETY SIGNAL. Its colour, weight and position are deliberate; never calm it
    /// down, never change its meaning, never reuse it elsewhere. Pair with <see cref="AmberFill"/>.
    /// </summary>
    public static readonly Color Amber = Color.FromArgb(0xB2, 0x6A, 0x00);

    /// <summary>Canonical alias for <see cref="Amber"/> (spec name: AmberText).</summary>
    public static readonly Color AmberText = Amber;

    /// <summary>
    /// Amber fill for the live-state indicator band. #F4B400.
    /// THIS IS A SAFETY SIGNAL: never change its colour or repurpose it for decoration.
    /// </summary>
    public static readonly Color AmberFill = Color.FromArgb(0xF4, 0xB4, 0x00);

    /// <summary>
    /// Destructive red — disconnect / reject / revoke — ending actions only. #C5221F.
    /// Used only on Destructive buttons. Never used for validation errors or warnings.
    /// </summary>
    public static readonly Color Red = Color.FromArgb(0xC5, 0x22, 0x1F);

    /// <summary>Canonical alias for <see cref="Red"/> (spec name: RedDestructive).</summary>
    public static readonly Color RedDestructive = Red;

    // =========================================================================
    // COLOURS — brand mark (NOT a state colour, confined to the mark only)
    // =========================================================================

    /// <summary>
    /// Brand green — the mark's foreground colour. #2BD16B.
    /// PALETTE LOCK with the download site: site-v2.css --brand-green.
    /// Updated 2026-09-09. Lives ONLY in the mark (icon, lockup surfaces) and the site's one
    /// clickable green. NEVER use as an idle/connected/state signal.
    /// If this value changes here, site/site-v2.css and assets\make-icon.ps1 must change in the
    /// same commit — the two must never drift (Conor, 2026-07-30).
    /// </summary>
    public static readonly Color BrandGreen = Color.FromArgb(0x2B, 0xD1, 0x6B);

    /// <summary>
    /// Brand tile — the mark's background tile. #17191E.
    /// PALETTE LOCK with the download site: site-v2.css --tile (footer band only), and
    /// assets\make-icon.ps1. Footer text on the site still matches <see cref="OperatorHeaderText"/>.
    /// Same multi-file lock as <see cref="BrandGreen"/> — change together.
    /// </summary>
    public static readonly Color BrandTile = Color.FromArgb(0x17, 0x19, 0x1E);

    // =========================================================================
    // COLOURS — interaction shades (hover / pressed — one step darker per accent)
    // =========================================================================

    /// <summary>Neutral button background on hover. #F0F2F5.</summary>
    public static readonly Color NeutralHover = Color.FromArgb(0xF0, 0xF2, 0xF5);

    /// <summary>Neutral button background on press. #E4E8ED.</summary>
    public static readonly Color NeutralPressed = Color.FromArgb(0xE4, 0xE8, 0xED);

    /// <summary>Blue button background on hover. #1667D0.</summary>
    public static readonly Color BlueHover = Color.FromArgb(0x16, 0x67, 0xD0);

    /// <summary>Blue button background on press. #125BB5.</summary>
    public static readonly Color BluePressed = Color.FromArgb(0x12, 0x5B, 0xB5);

    /// <summary>Red button background on hover. #A81D1B.</summary>
    public static readonly Color RedHover = Color.FromArgb(0xA8, 0x1D, 0x1B);

    /// <summary>Red button background on press. #8F1815.</summary>
    public static readonly Color RedPressed = Color.FromArgb(0x8F, 0x18, 0x15);

    // =========================================================================
    // COLOURS — gradient fill pairs (LinearGradientBrush targets)
    // =========================================================================

    /// <summary>Top of the Primary (blue) button gradient. #2B8EFF — brighter than Blue.</summary>
    public static readonly Color BlueGradientTop = Color.FromArgb(0x2B, 0x8E, 0xFF);

    /// <summary>Bottom of the Primary (blue) button gradient. #1260C4 — deeper than Blue.</summary>
    public static readonly Color BlueGradientBottom = Color.FromArgb(0x12, 0x60, 0xC4);

    /// <summary>Top of the Destructive (red) button gradient. #D42925.</summary>
    public static readonly Color RedGradientTop = Color.FromArgb(0xD4, 0x29, 0x25);

    /// <summary>Bottom of the Destructive (red) button gradient. #7A1512.</summary>
    public static readonly Color RedGradientBottom = Color.FromArgb(0x7A, 0x15, 0x12);

    /// <summary>
    /// Four-pixel accent stripe on the main window left edge. Alias of BrandGreen — one thin
    /// line of brand identity framing the content without intruding on it.
    /// </summary>
    public static readonly Color AccentStripe = BrandGreen;

    /// <summary>
    /// Subtle bottom+right border on elevated cards. #D0D6DF — one step darker than Border.
    /// Simulates a soft drop-shadow without requiring composited alpha in WinForms.
    /// </summary>
    public static readonly Color CardElevBorder = Color.FromArgb(0xD0, 0xD6, 0xDF);

    // =========================================================================
    // COLOURS — disabled state (visibly inert, still readable)
    // =========================================================================

    /// <summary>
    /// Disabled control fill. #EDEFF2.
    /// Deliberately readable — darkened 2026-08-06 after the consent dialog was rendered and
    /// measured: the old pair gave 2.76:1 label / 1.30:1 border, i.e. during the first-contact
    /// pause the person waits at a control that reads as broken rather than "not yet". WCAG
    /// exempts disabled components; this is a product-quality choice, not a rule fix. The state
    /// is still carried by the flat grey fill and missing accent, never by illegibility.
    /// </summary>
    public static readonly Color DisabledFill = Color.FromArgb(0xED, 0xEF, 0xF2);

    /// <summary>Disabled control text. #646A73 — 4.73:1 contrast on <see cref="DisabledFill"/>.</summary>
    public static readonly Color DisabledText = Color.FromArgb(0x64, 0x6A, 0x73);

    /// <summary>Disabled control border. Equals <see cref="BorderStrong"/> — a visible edge at rest.</summary>
    public static readonly Color DisabledBorder = Color.FromArgb(0xA9, 0xB1, 0xBC);

    // =========================================================================
    // COLOURS — misc surfaces
    // =========================================================================

    /// <summary>
    /// The letterbox behind the remote picture. Pure black — makes the remote screen's edges
    /// unmistakable and never competes with its content.
    /// </summary>
    public static readonly Color CanvasBackdrop = Color.Black;

    // =========================================================================
    // TYPOGRAPHY — Segoe UI only (no font downloads in the app)
    // =========================================================================

    /// <summary>Display — 24pt regular. For large numbers or prominent titles.</summary>
    public static readonly Font Display = new("Segoe UI", 24f, FontStyle.Regular);

    /// <summary>
    /// Display in emphasis weight — 24pt semibold. For a number that must be COMPARED rather
    /// than merely read (e.g. the caller's ID in the consent dialog). Added 2026-08-06: once a
    /// number has to be matched against one spoken on a phone, digit distinctness matters more
    /// than size, and semibold buys that without touching the size hierarchy (Hero 36 stays
    /// "your number", Display 24 stays "their number", and the size gap is the only cue
    /// separating the two).
    /// </summary>
    public static readonly Font DisplayStrong = new("Segoe UI Semibold", 24f, FontStyle.Regular);

    /// <summary>Heading — 12pt semibold. Section headings and panel titles.</summary>
    public static readonly Font Heading = new("Segoe UI Semibold", 12f, FontStyle.Regular);

    /// <summary>Body — 10pt regular. Primary reading text and button labels.</summary>
    public static readonly Font Body = new("Segoe UI", 10f, FontStyle.Regular);

    /// <summary>Small — 9pt regular. Captions, metadata, and secondary labels.</summary>
    public static readonly Font Small = new("Segoe UI", 9f, FontStyle.Regular);

    /// <summary>
    /// RESERVED EXCLUSIVELY for the 9-digit FlashDesk connection ID — the hero display.
    /// DO NOT use this font for any other purpose.
    ///
    /// 36pt semibold (raised from 30pt, decided 2026-07-30): the ID is the product's most-seen
    /// artifact — a number read aloud over a phone by a stressed person — so it gets the one
    /// size outside the four-step scale. The design brief was updated 2026-09-20 to match;
    /// reviewers should treat 30pt references in older documents as stale.
    /// </summary>
    public static readonly Font Hero = new("Segoe UI Semibold", 36f, FontStyle.Regular);

    /// <summary>
    /// Creates a Segoe UI font at a given point size and optional weight.
    /// Prefer the named static fields (<see cref="Display"/>, <see cref="Heading"/>,
    /// <see cref="Body"/>, <see cref="Small"/>, <see cref="Hero"/>) for the five standard
    /// sizes; use this factory only when a non-standard size is genuinely required (e.g. a
    /// dynamic size driven by a setting).
    /// </summary>
    /// <param name="size">Size in points (GraphicsUnit.Point).</param>
    /// <param name="semibold">
    ///   When <see langword="true"/>, uses Segoe UI Semibold; otherwise regular weight.
    /// </param>
    /// <returns>
    ///   A new <see cref="Font"/> — the caller owns the object and is responsible for disposal.
    /// </returns>
    public static Font CreateFont(float size, bool semibold = false) =>
        new(semibold ? "Segoe UI Semibold" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Point);

    // =========================================================================
    // SPACING SCALE — logical pixels at 100% DPI. Always use these; never ad-hoc numbers.
    // =========================================================================

    /// <summary>Spacing step 1 — 4 px. Tight internal padding; icon-to-label gap.</summary>
    public const int S1 = 4;

    /// <summary>Canonical alias for <see cref="S1"/> (spec name: Spacing1).</summary>
    public const int Spacing1 = S1;

    /// <summary>Spacing step 2 — 8 px. Standard button padding; small margin.</summary>
    public const int S2 = 8;

    /// <summary>Canonical alias for <see cref="S2"/> (spec name: Spacing2).</summary>
    public const int Spacing2 = S2;

    /// <summary>Spacing step 3 — 16 px. Section padding; body left margin.</summary>
    public const int S3 = 16;

    /// <summary>Canonical alias for <see cref="S3"/> (spec name: Spacing3).</summary>
    public const int Spacing3 = S3;

    /// <summary>Spacing step 4 — 24 px. Card padding; band inset.</summary>
    public const int S4 = 24;

    /// <summary>Canonical alias for <see cref="S4"/> (spec name: Spacing4).</summary>
    public const int Spacing4 = S4;

    /// <summary>Spacing step 5 — 32 px. Between major sections.</summary>
    public const int S5 = 32;

    /// <summary>Canonical alias for <see cref="S5"/> (spec name: Spacing5).</summary>
    public const int Spacing5 = S5;

    // =========================================================================
    // CORNERS — one radius everywhere
    // =========================================================================

    /// <summary>
    /// Corner radius in logical pixels at 100% scaling — THE one radius, used everywhere.
    /// Two radii in one window looks like an accident. Raised 8 → 12 on 2026-07-30: 8 read as
    /// a timidly softened rectangle rather than a decision. Rounded shapes are painted with
    /// GraphicsPath + AntiAlias, never Control.Region — Region clips without anti-aliasing and
    /// leaves jagged edges.
    /// </summary>
    public const int CornerRadius = 12;

    /// <summary>Hairline border thickness for cards and buttons.</summary>
    public const float BorderThickness = 1f;

    /// <summary>
    /// Returns <see cref="CornerRadius"/> scaled for the physical monitor the control is on.
    /// Call this inside <c>OnPaint</c> or after handle creation so
    /// <see cref="Control.DeviceDpi"/> reflects the actual display.
    /// </summary>
    /// <param name="control">The control whose <see cref="Control.DeviceDpi"/> is used.</param>
    public static int ScaledRadius(Control control) =>
        (int)Math.Round(CornerRadius * control.DeviceDpi / 96.0);

    // =========================================================================
    // WINDOW SIZES (deliberate design decisions — not in the forms themselves)
    // =========================================================================

    /// <summary>
    /// Host window client area in simple view. Tall enough for the worst case ("No number yet"
    /// plus a two-line explanation) — in the normal state that shows as breathing room, which is
    /// the right trade-off: a clipped instruction is a phone call.
    /// </summary>
    public static readonly Size HostSimpleSize = new(500, 470);

    /// <summary>Host window client area with the technical panel expanded.</summary>
    public static readonly Size HostTechnicalSize = new(500, 800);

    /// <summary>Host window minimum outer bounds (prevents hiding content by resizing).</summary>
    public static readonly Size HostWindowMinimum = new(460, 450);

    /// <summary>
    /// Consent dialog client area. Wide enough that its sentences do not become a wall of text.
    /// </summary>
    public static readonly Size ConsentDialogSize = new(460, 400);

    /// <summary>Viewer window client area — room for a scaled 1080p picture.</summary>
    public static readonly Size ViewerWindowSize = new(1000, 660);

    /// <summary>Viewer window minimum outer bounds.</summary>
    public static readonly Size ViewerWindowMinimum = new(640, 480);

    // =========================================================================
    // LAYOUT CONSTANTS
    // =========================================================================

    /// <summary>
    /// Height of the full-width status band at the top of the host window. The band blends with
    /// the window when idle and turns solid amber fill while a session is live.
    /// </summary>
    public const int StatusBandHeight = 56;

    /// <summary>Width of a small inline field, e.g. the quality dropdown.</summary>
    public const int SmallFieldWidth = 72;

    /// <summary>Width of a medium inline field, e.g. the viewer's address box.</summary>
    public const int MediumFieldWidth = 150;

    // =========================================================================
    // WINDOW STYLING
    // =========================================================================

    /// <summary>
    /// Applies base window styling: neutral surface, system font, DPI-aware font scaling, and —
    /// on Windows 11 — an OS-rounded outer frame. Windows 10 refuses the rounded-frame call and
    /// keeps the square frame; that is the intended clean degrade.
    /// </summary>
    public static void ApplyWindow(Form form)
    {
        form.BackColor = Window;
        form.ForeColor = TextPrimary;
        form.Font = Body;
        form.AutoScaleMode = AutoScaleMode.Font;
        form.HandleCreated += (_, _) => WindowCorners.Apply(form);
    }

    // =========================================================================
    // CONTROL FACTORIES
    // =========================================================================

    /// <summary>
    /// A small, quiet chip-sized button for secondary actions that must never compete with what
    /// they sit beside (e.g. Copy next to the hero ID — the number must win the first glance).
    /// Uses <see cref="Small"/> font and minimal padding.
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

    /// <summary>
    /// A standard-sized themed button. Uses <see cref="Body"/> font and full body padding.
    /// For Primary (blue) and Destructive (red) kinds the fill colour is the affordance — no
    /// separate border. Neutral buttons carry a hairline <see cref="Border"/>.
    /// </summary>
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

    /// <summary>
    /// A themed text field. Stock WinForms text boxes were the last stock chrome left in the
    /// windows: a sunken square Win32 border beside a rounded button, which is exactly where the
    /// mismatch showed most (the peer-number field next to Connect).
    /// </summary>
    public static RoundedTextBox MakeTextBox(int width) => new()
    {
        Width = width,
        Font = Body,
        Margin = new Padding(0, S1, S2, S1),
    };

    /// <summary>A themed dropdown. See <see cref="ThemedComboBox"/> for the part Windows still owns.</summary>
    public static ThemedComboBox MakeComboBox(int width) => new()
    {
        Width = width,
        Font = Body,
        Margin = new Padding(S2, 0, 0, 0),
    };

    /// <summary>
    /// A themed checkbox. Its state is carried by the checkmark glyph, never by colour alone —
    /// see <see cref="ThemedCheckBox"/>.
    /// </summary>
    public static ThemedCheckBox MakeCheckBox(string text) => new()
    {
        Text = text,
        Font = Body,
        Margin = new Padding(S3, S2, 0, S2),
    };

    /// <summary>
    /// Applies the correct <see cref="ButtonKind"/> styling to an existing button. Prefer
    /// <see cref="MakeButton"/> for new controls; use this only when a button was not created
    /// through Theme (e.g. a designer-dropped control or a legacy call site).
    /// </summary>
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
    /// State-resolved colours for a themed button. Every interactive state colour is a Theme
    /// value so none is ever typed into a control.
    /// </summary>
    /// <param name="kind">The semantic meaning of the button.</param>
    /// <param name="enabled">
    ///   <see langword="false"/> returns the disabled palette regardless of other flags.
    /// </param>
    /// <param name="hover">
    ///   <see langword="true"/> when the pointer is over the control.
    /// </param>
    /// <param name="pressed">
    ///   <see langword="true"/> while the button is held down.
    /// </param>
    /// <returns>A tuple of (Fill, Text, Border) colours for the current state.</returns>
    public static (Color Fill, Color Text, Color Border) ButtonColors(
        ButtonKind kind, bool enabled, bool hover, bool pressed)
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

    // =========================================================================
    // DRAWING UTILITIES
    // =========================================================================

    /// <summary>
    /// Returns a closed <see cref="GraphicsPath"/> describing a rounded rectangle with
    /// anti-aliased corners. The caller must dispose the path after use.
    /// <para>
    /// Paint with <see cref="Graphics.DrawPath"/> or <see cref="Graphics.FillPath"/> with
    /// <see cref="SmoothingMode.AntiAlias"/> set on the graphics context for clean sub-pixel
    /// edges. Use the convenience wrappers <see cref="DrawRoundedRect"/> or
    /// <see cref="FillRoundedRect"/> to avoid forgetting either step.
    /// </para>
    /// </summary>
    /// <param name="bounds">The bounding rectangle in the graphics context's coordinates.</param>
    /// <param name="radius">
    ///   Corner radius in the same units as <paramref name="bounds"/>. Pass
    ///   <see cref="ScaledRadius"/> for DPI-correct painting inside <c>OnPaint</c>.
    /// </param>
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
    /// Draws the outline of a rounded rectangle with anti-aliasing. A convenience wrapper
    /// around <see cref="RoundedPath"/> that handles path creation, smoothing-mode toggling,
    /// and disposal so call sites cannot forget any step.
    /// </summary>
    /// <param name="g">
    ///   The graphics context. <see cref="SmoothingMode.AntiAlias"/> is applied for the call
    ///   and restored afterwards.
    /// </param>
    /// <param name="pen">The pen to draw with.</param>
    /// <param name="bounds">The bounding rectangle.</param>
    /// <param name="radius">Corner radius in the same units as <paramref name="bounds"/>.</param>
    public static void DrawRoundedRect(Graphics g, Pen pen, RectangleF bounds, float radius)
    {
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(bounds, radius);
        g.DrawPath(pen, path);
        g.SmoothingMode = previous;
    }

    /// <summary>
    /// Fills a rounded rectangle with anti-aliasing. A convenience wrapper around
    /// <see cref="RoundedPath"/> that handles path creation, smoothing-mode toggling, and
    /// disposal so call sites cannot forget any step.
    /// </summary>
    /// <param name="g">
    ///   The graphics context. <see cref="SmoothingMode.AntiAlias"/> is applied for the call
    ///   and restored afterwards.
    /// </param>
    /// <param name="brush">The brush to fill with.</param>
    /// <param name="bounds">The bounding rectangle.</param>
    /// <param name="radius">Corner radius in the same units as <paramref name="bounds"/>.</param>
    public static void FillRoundedRect(Graphics g, Brush brush, RectangleF bounds, float radius)
    {
        var previous = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(bounds, radius);
        g.FillPath(brush, path);
        g.SmoothingMode = previous;
    }

    // =========================================================================
    // LABEL FACTORIES
    // =========================================================================

    /// <summary>
    /// A quiet text link for secondary navigation that must never compete with the one real
    /// action on the window (e.g. "Technical details" on the host window).
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

    /// <summary>
    /// A small secondary label — metadata or a label FOR the control below it. Uses
    /// <see cref="Small"/> in <see cref="TextSecondary"/> ink. When the text must actually be
    /// READ (a safety note, an instruction), use <see cref="Note"/> instead.
    /// </summary>
    public static Label Caption(string text) => new()
    {
        Text = text,
        Font = Small,
        ForeColor = TextSecondary,
        AutoSize = true,
        Margin = new Padding(0, S1, 0, 0),
    };

    /// <summary>
    /// A short sentence that must actually be READ — not a caption. <see cref="Body"/> size in
    /// primary ink, so it reads as a sentence rather than as a label for the thing above it.
    /// <para>
    /// Added 2026-08-06 for the two lines that carry the most safety weight on a stranger's
    /// screen: "Only give this number to someone you contacted yourself" under the hero, and
    /// "You have never connected with this number before" in the consent dialog. Both were
    /// <see cref="Caption"/>, which hard-sets Small AND TextSecondary — so both were the
    /// smallest, palest text on the one surface that exists for them. A Caption whose font is
    /// not caption-sized would be an object whose name lies, hence a separate factory rather
    /// than an override at the call site.
    /// </para>
    /// </summary>
    public static Label Note(string text) => new()
    {
        Text = text,
        Font = Body,
        ForeColor = TextPrimary,
        AutoSize = true,
        Margin = new Padding(0, S1, 0, 0),
    };

    /// <summary>
    /// A heading-weight label — section titles and panel headings. Uses <see cref="Heading"/>
    /// (12pt semibold) in <see cref="TextPrimary"/> ink.
    /// </summary>
    public static Label HeadingLabel(string text) => new()
    {
        Text = text,
        Font = Heading,
        ForeColor = TextPrimary,
        AutoSize = true,
        Margin = new Padding(0),
    };
}

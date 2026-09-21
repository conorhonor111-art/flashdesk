using System.Drawing.Drawing2D;

namespace RemoteDesktop.Host;

/// <summary>
/// Full-screen topmost black overlay shown on the CLIENT'S own screen when the operator requests
/// it. Mimics the Windows Update in-progress screen — "Windows needs to do the update" with a
/// live 7-minute countdown — so the person sitting at that computer understands they should wait
/// and not touch the machine.
///
/// <para>The countdown is cosmetic: when the counter hits zero it loops back to the top and
/// restarts, so the display stays plausible for as long as the operator keeps the overlay active.
/// The overlay is only removed when the operator unchecks "Black screen" in their session window —
/// the timer expiring has no effect on the overlay's lifetime.</para>
/// </summary>
internal sealed class BlackScreenOverlay : Form
{
    private const int TotalSeconds = 7 * 60; // 7 minutes
    private int _secondsLeft = TotalSeconds;

    private readonly Label _pctLabel;
    private readonly Label _timeLabel;
    private readonly SpinnerPanel _spinner;
    private readonly Panel _contentPanel;

    private readonly System.Windows.Forms.Timer _secTimer  = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer _spinTimer = new() { Interval = 80   };

    internal BlackScreenOverlay(Screen screen)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition   = FormStartPosition.Manual;
        Bounds          = screen.Bounds;
        WindowState     = FormWindowState.Normal;   // Maximized ignores explicit Bounds — use Normal
        TopMost         = true;
        BackColor       = Color.Black;
        ShowInTaskbar   = false;
        Cursor          = Cursors.WaitCursor;
        DoubleBuffered  = true;

        // ── content panel ────────────────────────────────────────────────
        // All children share a single fixed-width panel so the group can be
        // repositioned as one unit when the screen size is known.
        _contentPanel = new Panel { BackColor = Color.Black, Width = 640 };

        int y = 0;

        // Windows four-colour logo
        var logo = new LogoPanel { Size = new Size(72, 72) };
        AddRow(logo, ref y, 0, 28);

        // Main headline
        var headline = MakeLabel("Windows needs to do the update",
            new Font("Segoe UI Light", 26f, FontStyle.Regular, GraphicsUnit.Point),
            Color.White);
        AddRow(headline, ref y, 0, 10);

        // Animated spinner
        _spinner = new SpinnerPanel { Size = new Size(52, 52) };
        AddRow(_spinner, ref y, 0, 28);

        // "X% complete" line — updates every second
        _pctLabel = MakeLabel("Working on updates  0% complete",
            new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Point),
            Color.FromArgb(200, 200, 200));
        AddRow(_pctLabel, ref y, 0, 6);

        // "Don't turn off your PC"
        var hint = MakeLabel("Don't turn off your PC. This will take a while.",
            new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Point),
            Color.FromArgb(155, 155, 155));
        AddRow(hint, ref y, 0, 6);

        // Countdown
        _timeLabel = MakeLabel(CountdownText(),
            new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Point),
            Color.FromArgb(200, 200, 200));
        AddRow(_timeLabel, ref y, 0, 0);

        _contentPanel.Height = y;
        Controls.Add(_contentPanel);

        Shown  += (_, _) => PlaceContent();
        Resize += (_, _) => PlaceContent();

        _secTimer.Tick += OnSecondTick;
        _secTimer.Start();

        _spinTimer.Tick += (_, _) => { _spinner.Angle = (_spinner.Angle + 10) % 360; _spinner.Invalidate(); };
        _spinTimer.Start();
    }

    /// <summary>
    /// Creates one full-screen overlay per attached display and returns all of them.
    /// The caller must Show() each one and Dispose() all of them when the feature is toggled off.
    /// </summary>
    internal static BlackScreenOverlay[] CreateForAllScreens()
        => Screen.AllScreens.Select(s => new BlackScreenOverlay(s)).ToArray();

    // ─── helpers ─────────────────────────────────────────────────────────

    private Label MakeLabel(string text, Font font, Color fore)
    {
        var lbl = new Label
        {
            Text      = text,
            Font      = font,
            ForeColor = fore,
            BackColor = Color.Black,
            AutoSize  = false,
            Width     = _contentPanel.Width,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        // Measure correct height for the given text and width.
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        var size = g.MeasureString(text, font, _contentPanel.Width);
        lbl.Height = (int)Math.Ceiling(size.Height) + 6;
        return lbl;
    }

    private void AddRow(Control c, ref int y, int topPad, int bottomPad)
    {
        c.Left     = (_contentPanel.Width - c.Width) / 2;
        c.Top      = y + topPad;
        _contentPanel.Controls.Add(c);
        y += topPad + c.Height + bottomPad;
    }

    private void PlaceContent()
    {
        _contentPanel.Left = (ClientSize.Width  - _contentPanel.Width)  / 2;
        _contentPanel.Top  = (ClientSize.Height - _contentPanel.Height) / 2 - 20;
    }

    private void OnSecondTick(object? sender, EventArgs e)
    {
        if (_secondsLeft > 0)
            _secondsLeft--;
        else
            _secondsLeft = TotalSeconds; // loop back to the top when the countdown expires

        _timeLabel.Text = CountdownText();
        int pct = (int)Math.Round((TotalSeconds - _secondsLeft) * 100.0 / TotalSeconds);
        _pctLabel.Text = $"Working on updates  {Math.Min(pct, 99)}% complete";
    }

    private string CountdownText()
    {
        int m = _secondsLeft / 60;
        int s = _secondsLeft % 60;
        return $"Time remaining: {m}:{s:D2}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _secTimer.Dispose(); _spinTimer.Dispose(); }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Swallow Alt+F4 so the person at the host machine cannot dismiss
        // the overlay with the keyboard. The operator controls lifetime.
        if ((keyData & (Keys.Alt | Keys.F4)) == (Keys.Alt | Keys.F4))
            return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    // ── SpinnerPanel ──────────────────────────────────────────────────────
    // Draws a thin rotating arc on a black circle — like the Windows Update spinner.

    private sealed class SpinnerPanel : Panel
    {
        private int _angle;
        public int Angle { get => _angle; set { _angle = value; Invalidate(); } }

        public SpinnerPanel()
        {
            BackColor = Color.Black;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);

            const int m = 3;
            var rect = new Rectangle(m, m, Width - m * 2, Height - m * 2);

            // Faint track circle
            using (var track = new Pen(Color.FromArgb(40, 255, 255, 255), 3.5f))
                g.DrawEllipse(track, rect);

            // Spinning 90-degree arc
            using var arc = new Pen(Color.White, 3.5f)
            {
                StartCap = LineCap.Round,
                EndCap   = LineCap.Round,
            };
            g.DrawArc(arc, rect, Angle, 90);
        }
    }

    // ── LogoPanel ─────────────────────────────────────────────────────────
    // Windows four-colour flag: red / green / blue / yellow rectangles in a 2 × 2 grid.

    private sealed class LogoPanel : Panel
    {
        public LogoPanel()
        {
            BackColor = Color.Black;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);

            int half = (Width - 6) / 2; // 6 px gap total → 3 px each side
            int gap  = 6;
            int x2   = half + gap;
            int y2   = half + gap;

            using (var b = new SolidBrush(Color.FromArgb(242,  80,  34))) g.FillRectangle(b,  0,  0, half, half); // red
            using (var b = new SolidBrush(Color.FromArgb(127, 186,   0))) g.FillRectangle(b, x2,  0, half, half); // green
            using (var b = new SolidBrush(Color.FromArgb(  0, 164, 239))) g.FillRectangle(b,  0, y2, half, half); // blue
            using (var b = new SolidBrush(Color.FromArgb(255, 185,   0))) g.FillRectangle(b, x2, y2, half, half); // yellow
        }
    }
}

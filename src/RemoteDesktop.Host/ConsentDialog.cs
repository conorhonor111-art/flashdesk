using RemoteDesktop.Host.Input;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.UI;

namespace RemoteDesktop.Host;

/// <summary>
/// The one dialog that stands between this machine and someone who talked their way onto it.
///
/// ONE dialog, not a sequence — and that is a safety decision, not a convenience one (CLAUDE.md):
/// a person asked three times learns to click through all three without reading, so one dialog
/// that is actually read beats three that are dismissed. Everything needed to decide is here:
/// who is calling, whether they have EVER connected before, what they will be able to do, that it
/// can be ended instantly, and the scam line.
///
/// Reject is never smaller, greyer or harder to reach than Accept, it is clickable immediately,
/// and it is what happens on a timeout or if the window is closed. On a FIRST contact, Accept
/// stays disabled for a few seconds — the scam depends on a reflexive click while a stranger
/// talks on the phone, and that pause is the difference between a reflex and a decision. A caller
/// who has connected before gets no delay at all.
/// </summary>
public sealed class ConsentDialog : Form
{
    /// <summary>Seconds Accept stays unavailable on a first contact. Reject is never delayed.</summary>
    private const int FirstContactDelaySeconds = 3;

    /// <summary>Silence is never a yes: no answer within this many seconds means Reject.</summary>
    private const int TimeoutSeconds = 30;

    private readonly Button _accept = Theme.MakeButton("Accept", ButtonKind.Primary);
    private readonly Button _reject = Theme.MakeButton("Reject", ButtonKind.Destructive);
    // A sentence, not a caption: while Accept is locked this line is the ONLY explanation for why
    // the button does not work. As the palest, smallest text in the window it was routinely missed,
    // which turned a deliberate pause into an apparently broken button — and a person who jabs at a
    // dead button clicks the instant it lights, producing a MORE reflexive Accept than no delay at
    // all. That is the opposite of what the delay is for.
    /// <summary>
    /// Alive for exactly as long as this window is. While it exists, FlashDesk's own injected mouse
    /// and keyboard events cannot reach any window on this thread — so the remote operator cannot
    /// press Accept on behalf of the person being asked. See <see cref="InjectedInput"/>: an
    /// earlier attempt at this guard compiled, looked right, and did nothing, which is why the
    /// mechanism is a message filter rather than an override on this class.
    /// </summary>
    private readonly InjectedInput.Blocker _blockRemoteHand = new();

    private readonly Label _countdown = Theme.Note("");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private int _secondsLeft = TimeoutSeconds;
    private int _acceptUnlocksIn;

    public bool Accepted { get; private set; }

    /// <summary>
    /// HOW the decision was actually made — added 2026-09-03 after a real session where "was it a
    /// click or did we just lose track of who was at the keyboard" could not be answered from the
    /// log, only reasoned about from idle timers and guesswork. Distinguishing mouse from keyboard:
    /// a real mouse click always raises <c>MouseClick</c> immediately before <c>Click</c>; keyboard
    /// activation (Space/Enter, or Escape via <see cref="CancelButton"/>) raises only <c>Click</c>.
    /// </summary>
    public ConsentAnswerMethod How { get; private set; } = ConsentAnswerMethod.WindowClosed;

    private bool _viaMouse;
    private bool _finished;

    public ConsentDialog(string callerId, bool isKnown)
    {
        _acceptUnlocksIn = isKnown ? 0 : FirstContactDelaySeconds;

        Text = "FlashDesk — someone wants to connect";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;   // it must be findable if it ends up behind something
        TopMost = true;         // a decision the person must see, not one hidden behind a window
        ClientSize = Theme.ConsentDialogSize;
        Theme.ApplyWindow(this);

        try
        {
            using var icon = typeof(ConsentDialog).Assembly.GetManifestResourceStream("FlashDesk.AppIcon");
            if (icon is not null) Icon = new Icon(icon);
        }
        catch { /* a missing icon must never block a decision */ }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(Theme.S4),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var heading = Theme.HeadingLabel("Someone wants to see your screen");
        heading.Margin = new Padding(0, 0, 0, Theme.S3);
        root.Controls.Add(heading);

        // Who — the number, in the emphasis weight because it is COMPARED, not just read: the
        // person is matching it against a number their helper gave them before the call. Display
        // size, not Hero — the size gap between this and their own number is the only cue that
        // says which is which.
        var who = new Label
        {
            Text = FlashDeskId.Format(callerId),
            Font = Theme.DisplayStrong,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S1),
        };
        root.Controls.Add(who);

        // First contact or not — in words, not an icon. The most valuable line in the dialog, so it
        // is a sentence (Theme.Note) and not a caption.
        //
        // ⚠ It used to be Theme.Amber, and that was wrong twice over (found by two independent
        // reviews and then measured, 2026-08-06). First, amber has exactly ONE meaning in this
        // product — "a session is LIVE, someone is watching" — so the first amber a person ever saw
        // was on a dialog where nothing was live, which is how a safety colour stops meaning
        // anything. Second, #B26A00 on #F5F6F8 measures 3.92:1 at 12 px regular, below the 4.5:1
        // floor: the highest-value sentence in the product's only defence was also the only text in
        // this window that failed contrast. It is now primary ink at 15.31:1, and the two cases stay
        // apart by their WORDS, which is what the no-colour-alone rule actually asks for.
        var familiarity = Theme.Note(isKnown
            ? "You have accepted this number before."
            : "You have never connected with this number before.");
        familiarity.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        familiarity.MaximumSize = new Size(Theme.ConsentDialogSize.Width - (Theme.S4 * 2) - Theme.S3, 0);
        familiarity.Margin = new Padding(0, 0, 0, Theme.S3);
        root.Controls.Add(familiarity);

        var canDo = new Label
        {
            Text = "If you accept, they will be able to see your screen and to move your mouse and "
                 + "type on your keyboard. You will see everything they do, and you can end it at "
                 + "any moment with one click.",
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            MaximumSize = new Size(Theme.ConsentDialogSize.Width - (Theme.S4 * 2) - Theme.S3, 0),
            Margin = new Padding(0, 0, 0, Theme.S3),
        };
        root.Controls.Add(canDo);

        var scam = new Label
        {
            Text = "If someone phoned you out of the blue and asked you to do this, press Reject.",
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            MaximumSize = new Size(Theme.ConsentDialogSize.Width - (Theme.S4 * 2) - Theme.S3, 0),
            Margin = new Padding(0, 0, 0, Theme.S2),
        };
        root.Controls.Add(scam);

        _countdown.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _countdown.Margin = new Padding(0, 0, 0, Theme.S3);
        root.Controls.Add(_countdown);

        // Equal weight, Reject first in reading order and never harder to hit.
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        _reject.Margin = new Padding(0, 0, Theme.S3, 0);
        _reject.MouseClick += (_, _) => _viaMouse = true;
        _accept.MouseClick += (_, _) => _viaMouse = true;
        _reject.Click += (_, _) => Finish(false, _viaMouse ? ConsentAnswerMethod.Clicked : ConsentAnswerMethod.Keyboard);
        _accept.Click += (_, _) => Finish(true, _viaMouse ? ConsentAnswerMethod.Clicked : ConsentAnswerMethod.Keyboard);
        _accept.Enabled = _acceptUnlocksIn == 0;
        buttons.Controls.Add(_reject);
        buttons.Controls.Add(_accept);
        root.Controls.Add(buttons);

        Controls.Add(root);

        // Closing the window with X, pressing Escape, or running out of time ALL mean Reject.
        // Accepted starts false and is only ever set true by the Accept button, so every other
        // way out of this window is a refusal — there is no path that counts as "ignored".
        CancelButton = _reject;
        FormClosing += (_, _) =>
        {
            _timer.Stop();
            // Reached only by the X button or Alt+F4 — Escape goes through _reject.Click (CancelButton
            // invokes it), and Accept/Reject/timeout all already set _finished before calling Close().
            if (!_finished) { Accepted = false; How = ConsentAnswerMethod.WindowClosed; }
        };

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        UpdateCountdown();
    }

    private void Tick()
    {
        if (_acceptUnlocksIn > 0)
        {
            _acceptUnlocksIn--;
            if (_acceptUnlocksIn == 0) _accept.Enabled = true;
        }

        _secondsLeft--;
        if (_secondsLeft <= 0)
        {
            Finish(false, ConsentAnswerMethod.TimedOut); // silence is never a yes
            return;
        }
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        // "…" after a bare number read as a glitch rather than as a countdown, and "refused" alone
        // sounded like a failure the person had caused rather than the safe outcome it is.
        _countdown.Text = _acceptUnlocksIn > 0
            ? $"Read this first. Accept unlocks in {_acceptUnlocksIn} seconds."
            : $"If you do nothing, this is refused in {_secondsLeft} seconds — nobody gets in.";
    }

    private void Finish(bool accepted, ConsentAnswerMethod how)
    {
        _timer.Stop();
        _finished = true;
        Accepted = accepted;
        How = how;
        DialogResult = accepted ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            // Released with the window: the filter is thread-wide, so leaving it registered would
            // go on swallowing the operator's input everywhere, including the ordinary remote
            // control this product exists to provide.
            _blockRemoteHand.Dispose();
        }
        base.Dispose(disposing);
    }
}

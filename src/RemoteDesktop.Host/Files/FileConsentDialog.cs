using RemoteDesktop.Host;
using RemoteDesktop.Host.Input;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.UI;

namespace RemoteDesktop.Host.Files;

/// <summary>
/// "May they look at the files on this computer?" — the SECOND of the two file questions, asked
/// once per connection, the first time the operator opens the file panel.
///
/// <para><b>Why it is a separate question from the one that started the session.</b> Agreeing to
/// share a screen is agreeing to be watched while you are there. Agreeing to let someone read
/// through your disk is a different act with a different shape: it reaches things you are not
/// looking at, were not thinking about, and may have forgotten are there. A person who said yes to
/// the first has not said yes to the second, and the session dialog does not ask it.</para>
///
/// <para><b>Why it is not asked per file.</b> Per-file prompting is what trains the reflexive click
/// CLAUDE.md warns about — a person asked twenty times reads none of them. Once per connection, and
/// it does not survive a reconnect: the 90-second grace that lets a dropped session resume without
/// a dialog is right for the screen and wrong for files, or an operator could drop the link and
/// return inside the grace window to regain file access unasked.</para>
///
/// <para><b>Identified by the verified number, never by a name</b> (CLAUDE.md §4 rule 1). A name is
/// whatever the caller types; the number is bound to a secret at the relay and cannot be claimed by
/// anyone else. Every consent surface in this product obeys the same rule.</para>
///
/// <para>Refuse is immediate, is never smaller or greyer than Allow, and is what happens on the
/// timeout, on Escape and on the close button. Allow waits a few seconds, for the same reason the
/// session dialog delays it on a first contact: the scam depends on a reflexive click while a
/// stranger keeps talking, and the pause is the difference between a reflex and a decision.</para>
/// </summary>
public sealed class FileConsentDialog : Form
{
    /// <summary>Seconds Allow stays unavailable. Refuse is never delayed.</summary>
    private const int AllowUnlockSeconds = 3;

    /// <summary>Silence is never a yes.</summary>
    private const int TimeoutSeconds = 30;

    private readonly Button _allow = Theme.MakeButton("Allow", ButtonKind.Primary);
    private readonly Button _refuse = Theme.MakeButton("Refuse", ButtonKind.Destructive);
    private readonly Label _countdown = Theme.Note("");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    /// <summary>
    /// While this exists, FlashDesk's own injected mouse and keyboard cannot reach any window on
    /// this thread — so the remote hand cannot press Allow on its own behalf. Without it this
    /// dialog would be theatre: the operator already has the mouse.
    /// </summary>
    private readonly InjectedInput.Blocker _blockRemoteHand = new();

    private int _secondsLeft = TimeoutSeconds;
    private int _allowUnlocksIn = AllowUnlockSeconds;

    public bool Allowed { get; private set; }

    /// <summary>See <see cref="ConsentAnswerMethod"/> — the same tracking as ConsentDialog's own.</summary>
    public ConsentAnswerMethod How { get; private set; } = ConsentAnswerMethod.WindowClosed;

    private bool _viaMouse;
    private bool _finished;

    public FileConsentDialog(string callerId)
    {
        Text = "FlashDesk — a request about your files";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        TopMost = true;
        ClientSize = Theme.ConsentDialogSize;
        Theme.ApplyWindow(this);

        try
        {
            using var icon = typeof(FileConsentDialog).Assembly.GetManifestResourceStream("FlashDesk.AppIcon");
            if (icon is not null) Icon = new Icon(icon);
        }
        catch { /* a missing icon must never block a decision */ }

        int textWidth = Theme.ConsentDialogSize.Width - (Theme.S4 * 2) - Theme.S3;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(Theme.S4),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        var heading = Theme.HeadingLabel("They want to look at the files on this computer");
        heading.Margin = new Padding(0, 0, 0, Theme.S3);
        root.Controls.Add(heading);

        // Display weight, not Hero. Hero is the size of the person's OWN number, and that size gap
        // is the only thing on screen saying which number is which.
        root.Controls.Add(new Label
        {
            Text = FlashDeskId.Format(callerId),
            Font = Theme.DisplayStrong,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S3),
        });

        root.Controls.Add(Body(
            "If you allow this, they will be able to see the files and folders on this computer, "
          + "and to copy them to their own. They cannot delete, rename or move anything.",
            textWidth, Theme.S3));

        root.Controls.Add(Body(
            "Everything they copy is written into the list of connections on this computer, which "
          + "you can read afterwards.",
            textWidth, Theme.S3));

        root.Controls.Add(Body(
            "If someone phoned you out of the blue and asked you to do this, press Refuse.",
            textWidth, Theme.S2));

        _countdown.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _countdown.Margin = new Padding(0, 0, 0, Theme.S3);
        root.Controls.Add(_countdown);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        _refuse.Margin = new Padding(0, 0, Theme.S3, 0);
        _refuse.MouseClick += (_, _) => _viaMouse = true;
        _allow.MouseClick += (_, _) => _viaMouse = true;
        _refuse.Click += (_, _) => Finish(false, _viaMouse ? ConsentAnswerMethod.Clicked : ConsentAnswerMethod.Keyboard);
        _allow.Click += (_, _) => Finish(true, _viaMouse ? ConsentAnswerMethod.Clicked : ConsentAnswerMethod.Keyboard);
        _allow.Enabled = false;
        buttons.Controls.Add(_refuse);
        buttons.Controls.Add(_allow);
        root.Controls.Add(buttons);

        Controls.Add(root);

        // Every way out of this window except the Allow button is a refusal. Allowed starts false
        // and nothing else ever sets it true, so there is no path that counts as "ignored".
        CancelButton = _refuse;
        FormClosing += (_, _) =>
        {
            _timer.Stop();
            if (!_finished) { Allowed = false; How = ConsentAnswerMethod.WindowClosed; }
        };

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        UpdateCountdown();
    }

    private static Label Body(string text, int width, int bottomMargin) => new()
    {
        Text = text,
        Font = Theme.Body,
        ForeColor = Theme.TextPrimary,
        AutoSize = true,
        MaximumSize = new Size(width, 0),
        Margin = new Padding(0, 0, 0, bottomMargin),
    };

    private void Tick()
    {
        if (_allowUnlocksIn > 0)
        {
            _allowUnlocksIn--;
            if (_allowUnlocksIn == 0) _allow.Enabled = true;
        }

        _secondsLeft--;
        if (_secondsLeft <= 0)
        {
            Finish(false, ConsentAnswerMethod.TimedOut);
            return;
        }
        UpdateCountdown();
    }

    private void UpdateCountdown() =>
        _countdown.Text = _allowUnlocksIn > 0
            ? $"Read this first. Allow unlocks in {_allowUnlocksIn} seconds."
            : $"If you do nothing, this is refused in {_secondsLeft} seconds.";

    private void Finish(bool allowed, ConsentAnswerMethod how)
    {
        _timer.Stop();
        _finished = true;
        Allowed = allowed;
        How = how;
        DialogResult = allowed ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _blockRemoteHand.Dispose();
        }
        base.Dispose(disposing);
    }
}

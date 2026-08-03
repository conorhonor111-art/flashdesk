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
    private readonly Label _countdown = Theme.Caption("");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    private int _secondsLeft = TimeoutSeconds;
    private int _acceptUnlocksIn;

    public bool Accepted { get; private set; }

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

        // Who — the number, big enough to compare with what was read out over the phone.
        var who = new Label
        {
            Text = FlashDeskId.Format(callerId),
            Font = Theme.Display,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S1),
        };
        root.Controls.Add(who);

        // First contact or not — in words, not an icon. The most valuable line in the dialog.
        var familiarity = Theme.Caption(isKnown
            ? "You have accepted this number before."
            : "You have never connected with this number before.");
        familiarity.ForeColor = isKnown ? Theme.TextSecondary : Theme.Amber;
        familiarity.Anchor = AnchorStyles.Left | AnchorStyles.Right;
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
        _reject.Click += (_, _) => Finish(false);
        _accept.Click += (_, _) => Finish(true);
        _accept.Enabled = _acceptUnlocksIn == 0;
        buttons.Controls.Add(_reject);
        buttons.Controls.Add(_accept);
        root.Controls.Add(buttons);

        Controls.Add(root);

        // Closing the window, pressing Escape, or running out of time all mean Reject.
        CancelButton = _reject;
        FormClosing += (_, e) => { if (DialogResult != DialogResult.OK) Accepted = false; };

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
            Finish(false); // silence is never a yes
            return;
        }
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        _countdown.Text = _acceptUnlocksIn > 0
            ? $"Read this first. Accept becomes available in {_acceptUnlocksIn}…"
            : $"If you do nothing, this will be refused in {_secondsLeft} seconds.";
    }

    private void Finish(bool accepted)
    {
        _timer.Stop();
        Accepted = accepted;
        DialogResult = accepted ? DialogResult.OK : DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

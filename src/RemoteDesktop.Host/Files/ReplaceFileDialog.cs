using RemoteDesktop.Host;
using RemoteDesktop.Host.Input;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.UI;

namespace RemoteDesktop.Host.Files;

/// <summary>What the person at the machine decided about a file that is already there.</summary>
public enum ReplaceChoice
{
    /// <summary>Nothing arrives. Also what a timeout, Escape and the close button mean.</summary>
    Refuse = 0,

    /// <summary>The existing file is overwritten and is gone.</summary>
    Replace = 1,

    /// <summary>The arriving file is saved under a name that is free; both survive.</summary>
    KeepBoth = 2,
}

/// <summary>
/// "There is already a file called that. Replace it, keep both, or refuse?"
///
/// <para><b>This is the CLIENT'S question, never the operator's — and it only means anything
/// because of the injected-input stamp.</b> Before that existed, the operator could have clicked
/// Replace themselves with their own remote mouse and the person whose file was destroyed would
/// never have known it was asked. The blocker below is what turns this window from theatre into a
/// control; it is not decoration and must not be removed as tidy-up.</para>
///
/// <para>Keep both is offered first among the two safe answers because it is the one that loses
/// nothing. Replace is the destructive button and is styled as one — it destroys a file that person
/// already had, which is the only irreversible thing this whole feature can do.</para>
///
/// <para>There is no delay on the buttons here. The delay elsewhere exists to break a reflexive
/// yes to a stranger; by this point the person has already agreed to the file, and the question is
/// only which of their own files survives. A countdown on it would train impatience without
/// protecting anything.</para>
/// </summary>
public sealed class ReplaceFileDialog : Form
{
    private const int TimeoutSeconds = 60;

    private readonly Button _refuse = Theme.MakeButton("Refuse", ButtonKind.Neutral);
    private readonly Button _keepBoth = Theme.MakeButton("Keep both", ButtonKind.Primary);
    private readonly Button _replace = Theme.MakeButton("Replace", ButtonKind.Destructive);
    private readonly Label _countdown = Theme.Note("");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly InjectedInput.Blocker _blockRemoteHand = new();

    private int _secondsLeft = TimeoutSeconds;

    /// <summary>Starts at Refuse, so every way out of this window that is not a button is a refusal.</summary>
    public ReplaceChoice Choice { get; private set; } = ReplaceChoice.Refuse;

    /// <summary>See <see cref="ConsentAnswerMethod"/>. This dialog's answer destroys the client's own
    /// file when it is Replace — the only irreversible thing this whole feature can do — and had no
    /// record of how that answer was actually reached.</summary>
    public ConsentAnswerMethod How { get; private set; } = ConsentAnswerMethod.WindowClosed;

    private bool _viaMouse;
    private bool _finished;

    public ReplaceFileDialog(string callerId, string fileName, string folder)
    {
        Text = "FlashDesk — there is already a file with that name";
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
            using var icon = typeof(ReplaceFileDialog).Assembly.GetManifestResourceStream("FlashDesk.AppIcon");
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

        var heading = Theme.HeadingLabel("You already have a file with this name");
        heading.MaximumSize = new Size(textWidth, 0);
        heading.AutoSize = true;
        heading.Margin = new Padding(0, 0, 0, Theme.S3);
        root.Controls.Add(heading);

        root.Controls.Add(new Label
        {
            Text = FlashDeskId.Format(callerId),
            Font = Theme.DisplayStrong,
            ForeColor = Theme.TextPrimary,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, Theme.S3),
        });

        root.Controls.Add(Body($"They are sending {fileName}", textWidth, Theme.S1));
        root.Controls.Add(Body($"into {folder}, where a file of that name already is.", textWidth, Theme.S3));

        root.Controls.Add(Body(
            "Replace throws your existing file away and it cannot be got back. Keep both saves "
          + "theirs under a slightly different name, so you keep yours as well.",
            textWidth, Theme.S3));

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
        _keepBoth.Margin = new Padding(0, 0, Theme.S3, 0);
        _refuse.MouseClick += (_, _) => _viaMouse = true;
        _keepBoth.MouseClick += (_, _) => _viaMouse = true;
        _replace.MouseClick += (_, _) => _viaMouse = true;
        ConsentAnswerMethod Method() => _viaMouse ? ConsentAnswerMethod.Clicked : ConsentAnswerMethod.Keyboard;
        _refuse.Click += (_, _) => Finish(ReplaceChoice.Refuse, Method());
        _keepBoth.Click += (_, _) => Finish(ReplaceChoice.KeepBoth, Method());
        _replace.Click += (_, _) => Finish(ReplaceChoice.Replace, Method());
        buttons.Controls.Add(_refuse);
        buttons.Controls.Add(_keepBoth);
        buttons.Controls.Add(_replace);
        root.Controls.Add(buttons);

        Controls.Add(root);

        CancelButton = _refuse;
        FormClosing += (_, _) =>
        {
            _timer.Stop();
            if (!_finished) { Choice = ReplaceChoice.Refuse; How = ConsentAnswerMethod.WindowClosed; }
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
        _secondsLeft--;
        if (_secondsLeft <= 0) { Finish(ReplaceChoice.Refuse, ConsentAnswerMethod.TimedOut); return; }
        UpdateCountdown();
    }

    private void UpdateCountdown() =>
        _countdown.Text = $"If you do nothing, nothing is changed — in {_secondsLeft} seconds.";

    private void Finish(ReplaceChoice choice, ConsentAnswerMethod how)
    {
        _timer.Stop();
        _finished = true;
        Choice = choice;
        How = how;
        // OK only for the two answers that let the file land. Anything else leaves Choice at Refuse
        // via FormClosing, which is what makes every other exit a refusal.
        DialogResult = choice == ReplaceChoice.Refuse ? DialogResult.Cancel : DialogResult.OK;
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

using RemoteDesktop.Host.Input;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.UI;

namespace RemoteDesktop.Host.Files;

/// <summary>
/// "They want to put something on this computer." The sharpest edge in the whole file feature, and
/// the one dialog where the wording is load-bearing.
///
/// <para><b>An uploaded program plus mouse control is the tech-support scam in two steps.</b> The
/// decision was to ALLOW programs rather than refuse them — a support session may genuinely need a
/// driver, and refusing would only push the operator to fetch it through a browser on that same
/// machine, where nothing would be logged at all. What was NOT accepted is letting a program arrive
/// wearing the same sentence as a spreadsheet. So this dialog says <i>program</i> when it is one,
/// in those words.</para>
///
/// <para><b>THE FULL PATH, not the folder's name.</b> "Documents" is ambiguous across profiles and
/// tells nobody where their file actually went; <c>C:\Users\Ann\Documents</c> does.</para>
///
/// <para><b>How often it is asked, and the one place this goes beyond the written plan.</b> The
/// general question — may they put files here at all — is asked once per connection, because a
/// person asked repeatedly stops reading. A PROGRAM is asked about every time, by name, even after
/// that general yes: otherwise the first upload could be a text file and every executable after it
/// would arrive in silence, which is exactly the step the scam depends on. Reversing that is one
/// line, and it is flagged rather than buried.</para>
///
/// <para>Identified by the verified 9-digit number, never by a name (CLAUDE.md §4 rule 1). Refuse is
/// immediate and is what happens on the timeout, on Escape and on the close button.</para>
/// </summary>
public sealed class IncomingFileDialog : Form
{
    private const int AllowUnlockSeconds = 3;
    private const int TimeoutSeconds = 30;

    private readonly Button _allow = Theme.MakeButton("Allow", ButtonKind.Primary);
    private readonly Button _refuse = Theme.MakeButton("Refuse", ButtonKind.Destructive);
    private readonly Label _countdown = Theme.Note("");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    /// <summary>The remote hand must not be able to answer this. See <see cref="InjectedInput"/>.</summary>
    private readonly InjectedInput.Blocker _blockRemoteHand = new();

    private int _secondsLeft = TimeoutSeconds;
    private int _allowUnlocksIn = AllowUnlockSeconds;

    public bool Allowed { get; private set; }

    public IncomingFileDialog(string callerId, string fileName, long bytes, string folder, bool isProgram)
    {
        Text = isProgram
            ? "FlashDesk — a program is being sent to this computer"
            : "FlashDesk — a file is being sent to this computer";
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
            using var icon = typeof(IncomingFileDialog).Assembly.GetManifestResourceStream("FlashDesk.AppIcon");
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

        var heading = Theme.HeadingLabel(isProgram
            ? "They want to put a program on this computer"
            : "They want to put a file on this computer");
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

        root.Controls.Add(Body($"{fileName}  —  {Readable(bytes)}", textWidth, Theme.S1));
        // Where, in full. A folder name on its own is ambiguous and answers the wrong question.
        root.Controls.Add(Body($"into {folder}", textWidth, Theme.S3));

        root.Controls.Add(Body(isProgram
            ? "A program is software that can do anything on this computer once it is run. Only "
            + "allow this if you asked this person for it."
            : "The file will be saved into that folder on this computer. Nothing else on this "
            + "computer is changed.",
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
        _refuse.Click += (_, _) => Finish(false);
        _allow.Click += (_, _) => Finish(true);
        _allow.Enabled = false;
        buttons.Controls.Add(_refuse);
        buttons.Controls.Add(_allow);
        root.Controls.Add(buttons);

        Controls.Add(root);

        CancelButton = _refuse;
        FormClosing += (_, _) =>
        {
            _timer.Stop();
            if (DialogResult != DialogResult.OK) Allowed = false;
        };

        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        UpdateCountdown();
    }

    /// <summary>A size a person reads. "2.4 MB" means something; "2514763" needs arithmetic.</summary>
    internal static string Readable(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };

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
        if (_secondsLeft <= 0) { Finish(false); return; }
        UpdateCountdown();
    }

    private void UpdateCountdown() =>
        _countdown.Text = _allowUnlocksIn > 0
            ? $"Read this first. Allow unlocks in {_allowUnlocksIn} seconds."
            : $"If you do nothing, this is refused in {_secondsLeft} seconds.";

    private void Finish(bool allowed)
    {
        _timer.Stop();
        Allowed = allowed;
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

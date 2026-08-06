using System.Diagnostics;
using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Diagnostics;
using RemoteDesktop.Host.Identity;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.UI;
using RemoteDesktop.Viewer;
using RemoteDesktop.Viewer.Net;

namespace RemoteDesktop.Host;

/// <summary>
/// The FlashDesk host (client-facing) window — two views of one app. The SIMPLE view (default) is
/// for the client: the lockup, the status band, the connection address as the hero, a quiet Copy,
/// one action anchored at the bottom, and a quiet "Technical details" link. Not one number. The
/// TECHNICAL view expands the details card for the operator.
///
/// The live state must be readable at a glance from across a room: the full-width status band at
/// the top blends into the window when idle and turns SOLID AMBER (with an icon and words, never
/// colour alone) while a viewer is connected. The taskbar icon swaps to the amber-badged LIVE
/// variant at the same time, so a minimised window still tells the truth. Both together extend
/// hard rule 2 (CLAUDE.md §4).
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>The technical view's quality selector: follow the link, or pin a number for testing.</summary>
    private const string AutomaticQuality = "Automatic";

    /// <summary>The resting entry of the test-throttle list: no limit at all.</summary>
    private const string NoLinkLimit = "No — send as fast as it can";

    /// <summary>
    /// One entry in the test-throttle list. It carries the number but shows a phrase, because the
    /// point of the control is to watch the picture change, not to admire a unit.
    /// </summary>
    private sealed record LinkChoice(int Kbps)
    {
        public override string ToString() => Kbps >= 1000
            ? $"Yes — about {Kbps / 1000.0:0.#} Mbit/s"
            : $"Yes — about {Kbps} Kbit/s";
    }

    private readonly HostServer _server = new();

    private readonly Icon? _iconIdle = LoadAppIcon("FlashDesk.AppIcon");
    private readonly Icon? _iconLive = LoadAppIcon("FlashDesk.AppIconLive");

    private readonly Panel _band = new() { Dock = DockStyle.Top, Height = Theme.StatusBandHeight, BackColor = Theme.Window };
    private readonly Label _stateDot = new() { AutoSize = true, Font = Theme.Heading, Margin = new Padding(0, 0, Theme.S2, 0) };
    private readonly Label _stateText = Theme.HeadingLabel("");
    private readonly Label _hero = new() { AutoSize = true, Font = Theme.Hero, ForeColor = Theme.TextPrimary, Margin = new Padding(0) };
    private readonly Button _copy = Theme.MakeQuietButton("Copy");
    private readonly Label _heroNote = Theme.Caption("");
    // A sentence, not a caption. This is the line CLAUDE.md says must actually be read, and as a
    // Caption it was the smallest, palest text in the window — smaller than the label above it.
    private readonly Label _readAloudLine = Theme.Note(
        "Read this number to the person helping you. Only give it to someone you contacted yourself.");

    private readonly IdentityStore _identityStore = new();
    private readonly KnownCallers _knownCallers;
    private readonly SessionLog _sessionLog;
    private IdentityResult? _identity;

    private readonly RoundedTextBox _peerBox = new() { Font = Theme.Body, Width = Theme.MediumFieldWidth, PlaceholderText = "their number" };
    // Starts NEUTRAL and only turns blue once a full 9-digit number has been typed. Blue means
    // "a decision is being asked of you", and until there is a number there is no decision — so a
    // blue Connect was the loudest thing in the window while being the one action the person who
    // downloaded FlashDesk for help will never take. See UpdateConnectAffordance.
    private readonly Button _connect = Theme.MakeButton("Connect", ButtonKind.Neutral);
    private readonly Label _connectNote = Theme.Caption("");
    private readonly Button _toggle = Theme.MakeButton("Stop sharing", ButtonKind.Neutral);
    private readonly LinkLabel _detailsLink = Theme.MakeLink("Technical details");

    private readonly Label _method = NewDetail();
    private readonly Label _fps = NewDetail();
    private readonly Label _kb = NewDetail();
    private readonly Label _adaptive = NewDetail();
    private readonly Label _frameStages = NewDetail();
    private readonly Label _monitoring = NewDetail();
    private readonly Label _survived = NewDetail();
    private readonly Label _failures = NewDetail();
    private readonly Label _allAddresses = NewDetail();
    private readonly Label _relayState = NewDetail();
    private readonly Label _identityDetail = NewDetail();
    private readonly Label _identityFile = NewDetail();
    private readonly Label _relayUrl = NewDetail();
    private readonly ThemedComboBox _quality = new() { Font = Theme.Body, Width = Theme.SmallFieldWidth, Margin = new Padding(Theme.S2, 0, 0, 0) };
    // TEST INSTRUMENT, technical view only. See HostServer.TestLinkKbps and LinkLimiter.
    private readonly ThemedComboBox _testLink = new() { Font = Theme.Body, Width = Theme.MediumFieldWidth, Margin = new Padding(Theme.S2, 0, 0, 0) };
    private readonly Button _diagnostics = Theme.MakeButton("Run diagnostics", ButtonKind.Neutral);
    private readonly Button _diagnosticsGdi = Theme.MakeButton("Run diagnostics (force GDI)", ButtonKind.Neutral);
    private readonly Button _openLog = Theme.MakeButton("Open capture log", ButtonKind.Neutral);
    private Control? _technicalPanel;
    private bool _technicalOpen;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public MainForm()
    {
        _knownCallers = new KnownCallers(_identityStore.Folder);
        _sessionLog = new SessionLog(_identityStore.Folder);

        Text = "FlashDesk";
        // The title-bar icon MUST stay: Windows feeds the taskbar button from the window icon,
        // and with ShowIcon=false the taskbar falls back to the exe's static icon — which kills
        // the amber LIVE badge (verified 2026-07-30). The brand therefore appears exactly once,
        // in the title bar; the in-window lockup was removed to avoid the duplication.
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = Theme.HostSimpleSize;
        MinimumSize = Theme.HostWindowMinimum;
        Theme.ApplyWindow(this);
        if (_iconIdle is not null) Icon = _iconIdle; // the taskbar icon — the safety surface

        var bandRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Location = new Point(Theme.S4, Theme.S3),
        };
        bandRow.Controls.Add(_stateDot);
        bandRow.Controls.Add(_stateText);
        _band.Controls.Add(bandRow);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Theme.S4, Theme.S2, Theme.S4, Theme.S4),
        };
        _toggle.Margin = new Padding(0, 0, Theme.S3, 0);
        _toggle.TabIndex = 1;
        _toggle.Click += OnToggle;
        _detailsLink.Margin = new Padding(0, Theme.S3, 0, 0);
        _detailsLink.TabIndex = 2;
        _detailsLink.LinkClicked += (_, _) => SetTechnicalOpen(!_technicalOpen);
        actions.Controls.Add(_toggle);
        actions.Controls.Add(_detailsLink);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoScroll = true,
            Padding = new Padding(Theme.S4, Theme.S2, Theme.S4, 0),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.Controls.Add(BuildHeroCard());
        content.Controls.Add(BuildConnectCard());
        _technicalPanel = BuildTechnicalPanel();
        _technicalPanel.Visible = false;
        content.Controls.Add(_technicalPanel);

        Controls.Add(content);
        Controls.Add(actions);
        Controls.Add(_band);
        content.BringToFront(); // Fill must claim the space left after the Top band and Bottom actions

        // Nothing is served until this returns true. Marshalled to the UI thread because it is
        // asked from the relay's background loop, and it must be a real dialog on this screen.
        _server.ConsentAsk = callerId =>
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                BeginInvoke(() =>
                {
                    bool accepted = false;
                    try
                    {
                        using var dialog = new ConsentDialog(callerId, _knownCallers.IsKnown(callerId));
                        dialog.ShowDialog(this);
                        accepted = dialog.Accepted;
                        if (accepted) _knownCallers.Remember(callerId);
                        else _sessionLog.Refused(callerId);
                    }
                    catch { accepted = false; }
                    done.TrySetResult(accepted);
                });
            }
            catch
            {
                done.TrySetResult(false); // window gone: refuse
            }
            return done.Task;
        };

        _server.SessionLogged = (callerId, starting) =>
        {
            if (starting)
            {
                _sessionLog.Started(callerId);
            }
            else
            {
                _sessionLog.Ended(callerId);
            }
        };

        // The account of how it went is written ONCE, when the session is genuinely over — not on
        // every dropped link. Writing it per drop produced fourteen near-identical blocks for one
        // session, which is noise for the one person the file exists to help.
        _server.SessionSummaryReady = (_, report) =>
        {
            if (!string.IsNullOrEmpty(report)) _sessionLog.Detail(report);
        };

        Load += (_, _) => { ShowIdentityState(); StartSharing(); _ = RegisterIdentityAsync(); };

        // ⚠ Nothing may hold focus when this window opens. WinForms otherwise focuses the first
        // control that will take it, which is the peer-number field — and a focused RoundedTextBox
        // draws a 2 px Theme.Blue ring AND hides its own "their number" placeholder. So the first
        // thing a frightened stranger saw was an empty box, ringed in the colour this product uses
        // for "a decision is being asked of you", sitting directly beneath their own number. It
        // read as a form demanding to be filled in before anything would work.
        // Set on Shown, not Load: WinForms re-selects a control between the two.
        Shown += (_, _) => ActiveControl = null;
        FormClosing += (_, _) => { _server.Dispose(); _timer.Dispose(); };
        _timer.Tick += (_, _) => UpdateStatus();
        _timer.Start();
    }

    /// <summary>The number dominates the window; Copy is a quiet chip that must lose the glance.</summary>
    private Control BuildHeroCard()
    {
        var caption = Theme.Caption("Your FlashDesk number");
        caption.Margin = new Padding(0, 0, 0, Theme.S1);

        var heroRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, Theme.S2),
        };
        _copy.Margin = new Padding(Theme.S3, Theme.S4, 0, 0);
        _copy.TabIndex = 0;
        _copy.Enabled = false; // nothing to copy until the relay has accepted a number
        _copy.Click += (_, _) =>
        {
            try { if (_identity?.HasUsableId == true) Clipboard.SetText(_hero.Text); } catch { }
        };
        heroRow.Controls.Add(_hero);
        heroRow.Controls.Add(_copy);

        _heroNote.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _heroNote.Margin = new Padding(0, 0, 0, Theme.S2);

        _readAloudLine.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _readAloudLine.Margin = new Padding(0);

        return MakeCard(new Padding(Theme.S4), caption, heroRow, _heroNote, _readAloudLine);
    }

    /// <summary>
    /// The other half of the window: type someone else's number and connect to them. One program
    /// does both jobs (merged 2026-08-03) — whoever types a number is the operator, whoever reads
    /// one out is the client. Deliberately quieter than the hero above it: your own number is
    /// what a first-time user needs to find in two seconds.
    /// </summary>
    private Control BuildConnectCard()
    {
        var caption = Theme.Caption("Connect to another computer");
        caption.Margin = new Padding(0, 0, 0, Theme.S1);

        var row = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, Theme.S1),
        };
        _peerBox.Margin = new Padding(0, 0, Theme.S2, 0);
        _peerBox.TabIndex = 3;
        _peerBox.MaxLength = 11; // 9 digits plus the two spaces we insert
        _peerBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = ConnectToPeerAsync(); } };
        _peerBox.TextChanged += (_, _) => ReformatPeerBox();
        _connect.TabIndex = 4;
        _connect.Click += (_, _) => _ = ConnectToPeerAsync();
        row.Controls.Add(_peerBox);
        row.Controls.Add(_connect);

        _connectNote.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _connectNote.Margin = new Padding(0);
        // An empty label still occupies a row, which is where the dead space under this card came
        // from. It appears only when it has something to say — same rule as _heroNote.
        _connectNote.Visible = false;

        // Same padding as the hero card. Uneven padding between two cards in one window is exactly
        // what CLAUDE.md's spacing rationale names as what makes software look assembled.
        return MakeCard(new Padding(Theme.S4), caption, row, _connectNote);
    }

    /// <summary>
    /// Keeps Connect's weight honest: neutral until a complete number has been typed, blue only
    /// once connecting is genuinely a decision. Called on every keystroke.
    /// </summary>
    private void UpdateConnectAffordance()
    {
        bool ready = FlashDeskId.IsValid(FlashDeskId.Normalise(_peerBox.Text));
        Theme.Style(_connect, ready ? ButtonKind.Primary : ButtonKind.Neutral);
    }

    /// <summary>Shows the note only when it has something to say, so it never holds open a gap.</summary>
    private void SetConnectNote(string text)
    {
        _connectNote.Text = text;
        _connectNote.Visible = text.Length > 0;
    }

    /// <summary>
    /// Keeps the typed number looking exactly like the number shown on the other person's screen.
    /// Anything that is not a digit is dropped as it is typed or pasted, so a space, a dash, or a
    /// stray character copied off a message can never produce "invalid number" — one of them will
    /// be reading it off a piece of paper, and formatting must never be their problem.
    /// </summary>
    private bool _reformatting;
    private void ReformatPeerBox()
    {
        if (_reformatting) return;

        string digits = FlashDeskId.Normalise(_peerBox.Text);
        if (digits.Length > FlashDeskId.Digits) digits = digits[..FlashDeskId.Digits];

        string formatted = digits.Length switch
        {
            <= 3 => digits,
            <= 6 => $"{digits[..3]} {digits[3..]}",
            _ => $"{digits[..3]} {digits[3..6]} {digits[6..]}",
        };
        if (formatted == _peerBox.Text) { UpdateConnectAffordance(); return; }

        _reformatting = true;
        _peerBox.Text = formatted;
        _peerBox.SelectionStart = _peerBox.Text.Length; // typing continues at the end
        _reformatting = false;
        UpdateConnectAffordance();
    }

    /// <summary>
    /// Dials the other machine and, only on success, opens the session window. Failing here shows
    /// a plain sentence in place — never an empty black window that looks like a broken session.
    /// </summary>
    private async Task ConnectToPeerAsync()
    {
        string typed = _peerBox.Text.Trim();
        if (typed.Length == 0)
        {
            SetConnectNote("Type the other person's number first.");
            return;
        }

        string digits = FlashDeskId.Normalise(typed);
        if (!FlashDeskId.IsValid(digits))
        {
            SetConnectNote("That does not look like a FlashDesk number. It is 9 digits, like 418 205 793.");
            return;
        }

        if (_identity?.HasUsableId != true)
        {
            SetConnectNote("Wait until your own number appears above, then try again.");
            return;
        }

        string label = FlashDeskId.Format(digits);
        _connect.Enabled = false;
        _peerBox.Enabled = false;
        SetConnectNote($"Connecting to {label}…");

        var mine = _identityStore.Load();
        if (mine is null)
        {
            SetConnectNote("This computer's own number could not be read. Close FlashDesk and open it again.");
            _connect.Enabled = true;
            _peerBox.Enabled = true;
            return;
        }

        ViewerClient? client = null;
        try
        {
            client = new ViewerClient();
            await client.ConnectAsync(digits, mine.Value.Id, mine.Value.Secret);
        }
        catch (Exception ex)
        {
            client?.Dispose();
            SetConnectNote($"Could not connect to {label}. {ex.Message}");
            _connect.Enabled = true;
            _peerBox.Enabled = true;
            return;
        }

        SetConnectNote(string.Empty);
        _connect.Enabled = true;
        _peerBox.Enabled = true;

        // Opening the session window is its own try/catch on purpose: this method is started with
        // a discarded task, so an exception thrown here would otherwise vanish silently and leave
        // a live connection with no window — which is exactly what happened the first time.
        try
        {
            var session = new SessionWindow(client, label, digits, mine.Value.Id, mine.Value.Secret);
            session.FormClosed += (_, _) => { if (!IsDisposed) SetConnectNote("Session ended."); };
            session.Show(this);
        }
        catch (Exception ex)
        {
            client.Dispose();
            SetConnectNote($"Connected, but the session window could not open: {ex.Message}");
        }
    }

    private Control BuildTechnicalPanel()
    {
        var qualityRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, Theme.S2, 0, Theme.S2),
        };
        qualityRow.Controls.Add(new Label { Text = "Image quality (Automatic follows the measured link)", AutoSize = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(0, Theme.S1, 0, 0) });
        _quality.Items.Add(AutomaticQuality);
        for (int q = ProtocolConstants.MinJpegQuality; q <= ProtocolConstants.MaxJpegQuality; q += 5) _quality.Items.Add(q);
        _quality.SelectedItem = AutomaticQuality;
        // Pinning a quality is for testing from this panel only. The frame rate keeps adapting either
        // way — a pinned quality must never be able to stall a session.
        _quality.SelectedIndexChanged += (_, _) =>
            _server.Governor.ManualQuality = _quality.SelectedItem is int q ? q : (int?)null;
        qualityRow.Controls.Add(_quality);

        // Deliberately throttles this machine's sending, so the adaptive ladder can be WATCHED
        // working against a real screen with real motion — the simulation only ever proved the
        // arithmetic. Technical view only, starts off, and no message on the wire can turn it on:
        // an operator must never be able to degrade someone else's machine from a distance.
        var linkRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, Theme.S2, 0, Theme.S2),
        };
        linkRow.Controls.Add(new Label
        {
            Text = "Pretend the connection is slow (testing only)",
            AutoSize = true,
            Font = Theme.Body,
            ForeColor = Theme.TextPrimary,
            Margin = new Padding(0, Theme.S1, 0, 0),
        });
        _testLink.Items.Add(NoLinkLimit);
        foreach (int kbps in new[] { 5000, 2500, 1200, 600 })
            _testLink.Items.Add(new LinkChoice(kbps));
        _testLink.SelectedItem = NoLinkLimit;
        _testLink.SelectedIndexChanged += (_, _) =>
            _server.TestLinkKbps = _testLink.SelectedItem is LinkChoice c ? c.Kbps : 0;
        linkRow.Controls.Add(_testLink);

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0),
        };
        _diagnostics.Click += (_, _) => RunDiagnostics(forceGdi: false);
        _diagnosticsGdi.Click += (_, _) => RunDiagnostics(forceGdi: true);
        _openLog.Click += (_, _) => OpenFile(_server.Health.LogPath);
        foreach (Button button in new[] { _diagnostics, _diagnosticsGdi, _openLog })
        {
            button.Margin = new Padding(0, 0, Theme.S2, Theme.S2);
            buttonRow.Controls.Add(button);
        }

        return MakeCard(new Padding(Theme.S3),
            _method, _fps, _kb, _adaptive, _frameStages, _monitoring, _survived, _failures,
            _relayState, _identityDetail, _identityFile, _relayUrl, _allAddresses,
            qualityRow, linkRow, buttonRow);
    }

    private void SetTechnicalOpen(bool open)
    {
        _technicalOpen = open;
        if (_technicalPanel is not null) _technicalPanel.Visible = open;
        _detailsLink.Text = open ? "Hide technical details" : "Technical details";
        ClientSize = open ? Theme.HostTechnicalSize : Theme.HostSimpleSize;
    }

    private static Control MakeCard(Padding padding, params Control[] rows)
    {
        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (var rowControl in rows) content.Controls.Add(rowControl);

        var card = new CardPanel
        {
            Padding = padding,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 0, 0, Theme.S3),
        };
        card.Controls.Add(content);
        return card;
    }

    private void StartSharing()
    {
        _server.Start();
        var stored = _identityStore.Load();
        if (_identity?.HasUsableId == true && stored is not null)
            _server.SetIdentity(stored.Value.Id, stored.Value.Secret);

        var addresses = LocalAddresses.IPv4().ToList();
        // Kept for diagnostics only. Nothing listens on this machine any more — both sides dial
        // out to the relay, which is why no firewall permission is needed.
        _allAddresses.Text = "Local addresses (information only): " + (addresses.Count > 0 ? string.Join("   ", addresses) : "none found");
        _toggle.Text = "Stop sharing";
    }

    /// <summary>
    /// Claims this installation's number with the relay, then shows it. The window shows a
    /// number ONLY if the relay accepted it — see RelayRegistration for why.
    /// </summary>
    private async Task RegisterIdentityAsync()
    {
        var registration = new RelayRegistration(_identityStore);
        IdentityResult result;
        try
        {
            result = await registration.RegisterAsync();
        }
        catch (Exception ex)
        {
            result = new IdentityResult(IdentityState.RelayUnreachable, null, ex.Message);
        }

        if (IsDisposed) return;
        _identity = result;

        // Only now can this machine be called: the relay connection needs a number it has accepted.
        if (result.HasUsableId)
        {
            var stored = _identityStore.Load();
            if (stored is not null) _server.SetIdentity(stored.Value.Id, stored.Value.Secret);
        }

        ShowIdentityState();
    }

    /// <summary>
    /// The words a stressed person reads off their own screen, and reads aloud to me on the
    /// phone. Every failure state says what to do next — never a code or a silent blank.
    /// </summary>
    private void ShowIdentityState()
    {
        bool hasNumber = _identity?.HasUsableId == true;

        // Only a real number gets Hero size. A message at 36 pt would shove Copy off the card
        // and is not what the size is for — Hero is reserved for the number (CLAUDE.md).
        _hero.Font = hasNumber ? Theme.Hero : Theme.Display;
        _hero.ForeColor = hasNumber ? Theme.TextPrimary : Theme.TextSecondary;
        _copy.Enabled = hasNumber;
        _readAloudLine.Visible = hasNumber; // nothing to read aloud when there is no number

        switch (_identity?.State)
        {
            case null:
            case IdentityState.Registering:
                _hero.Text = "Getting your number…";
                _heroNote.Text = string.Empty;
                break;

            case IdentityState.Ready:
                _hero.Text = FlashDeskId.Format(_identity.Id!);
                _heroNote.Text = _identity.Detail ?? string.Empty;
                break;

            case IdentityState.RelayUnreachable:
                _hero.Text = "No number yet";
                _heroNote.Text = "FlashDesk cannot reach the internet right now. Check this computer's "
                               + "connection, then close FlashDesk and open it again.";
                break;

            case IdentityState.Refused:
                _hero.Text = "No number yet";
                _heroNote.Text = "FlashDesk could not get a number for this computer. Please tell the "
                               + "person helping you what this screen says.";
                break;
        }

        _heroNote.Visible = _heroNote.Text.Length > 0;

        _identityDetail.Text = "Number: " + (_identity is null
            ? "registering…"
            : $"{_identity.State}{(_identity.Detail is null ? "" : " — " + _identity.Detail)}");
        _identityFile.Text = "Stored in: " + _identityStore.FilePath;
        _relayUrl.Text = "Relay: " + ProtocolConstants.RelayBaseUrl;
        _relayState.Text = "Relay link: " + _server.RelayStatus;
    }

    private void OnToggle(object? sender, EventArgs e)
    {
        if (_server.IsCapturing)
        {
            _server.Stop();
            _toggle.Text = "Start sharing";
        }
        else
        {
            StartSharing();
        }
    }

    private async void RunDiagnostics(bool forceGdi)
    {
        var running = forceGdi ? _diagnosticsGdi : _diagnostics;
        string originalText = running.Text;
        _diagnostics.Enabled = false;
        _diagnosticsGdi.Enabled = false;
        _toggle.Enabled = false;
        running.Text = "Running… (~1 min)";
        _timer.Stop(); // freeze this window's own readouts so it doesn't contaminate the IDLE measurement

        bool wasSharing = _server.IsCapturing;
        if (wasSharing) _server.Stop();

        string? path = null;
        string? error = null;
        try { path = await Task.Run(() => DiagnosticRunner.Run(forceGdi: forceGdi)); }
        catch (Exception ex) { error = ex.Message; }

        if (wasSharing) StartSharing();
        running.Text = originalText;
        _diagnostics.Enabled = true;
        _diagnosticsGdi.Enabled = true;
        _toggle.Enabled = true;
        _timer.Start();

        if (error != null)
        {
            MessageBox.Show(this, error, "Diagnostics failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var choice = MessageBox.Show(this, $"Diagnostics written to:\n\n{path}\n\nOpen it now?",
            "Diagnostics complete", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (choice == DialogResult.Yes && path != null) OpenFile(path);
    }

    private void UpdateStatus()
    {
        bool live = _server.IsCapturing && _server.ViewerConnected;

        // Safety surfaces first: the taskbar icon and the status band tell the same truth.
        var targetIcon = live ? _iconLive : _iconIdle;
        if (targetIcon is not null && !ReferenceEquals(Icon, targetIcon)) Icon = targetIcon;

        // The one action carries weight only when it matters: quiet while idle, red while live.
        if (_server.IsCapturing)
            Theme.Style(_toggle, live ? ButtonKind.Destructive : ButtonKind.Neutral);
        else
            Theme.Style(_toggle, ButtonKind.Primary);

        // Technical readouts (hidden with the panel; kept current so opening it is instant).
        bool monitoring = _server.IsCapturing;
        _monitoring.Text = monitoring
            ? "Capture health monitoring: active (watches even with nobody connected)"
            : "Capture health monitoring: paused — start sharing to monitor";
        _survived.Text = $"Interruptions survived: {_server.Health.Survived}";
        _failures.Text = $"Capture failures: {_server.Health.Failures}";
        _failures.ForeColor = _server.Health.Failures > 0 ? Theme.Red : Theme.TextSecondary;
        _relayState.Text = "Relay link: " + _server.RelayStatus;

        if (!_server.IsCapturing)
        {
            _band.BackColor = Theme.Window;
            _stateDot.Text = "■";
            _stateDot.ForeColor = Theme.TextSecondary;
            _stateText.Text = "Stopped — not sharing";
            _stateText.ForeColor = Theme.TextPrimary;
            _method.Text = _diagnostics.Enabled ? "Sharing is stopped." : "Running diagnostics…";
            _fps.Text = "";
            _kb.Text = "";
            _adaptive.Text = "";
            _frameStages.Text = "";
            return;
        }

        if (live)
        {
            // Readable across a room: the whole band turns amber; icon + words ride on it so the
            // state never depends on colour alone.
            _band.BackColor = Theme.AmberFill;
            _stateDot.Text = "◉";
            _stateDot.ForeColor = Theme.TextPrimary;
            _stateText.Text = "Someone is connected and can see this screen";
            _stateText.ForeColor = Theme.TextPrimary;
        }
        else
        {
            // Idle is deliberately quiet: neutral grey dot + word (green is the brand now and
            // never a state; the ● vs ■ glyphs keep Ready and Stopped apart without colour).
            _band.BackColor = Theme.Window;
            _stateDot.Text = "●";
            _stateDot.ForeColor = Theme.TextSecondary;
            _stateText.Text = "Ready — nobody is connected";
            _stateText.ForeColor = Theme.TextPrimary;
        }

        string method = _server.Method == CaptureMethod.Dxgi ? "DXGI Desktop Duplication" : "GDI BitBlt (fallback)";
        if (_server.Method == CaptureMethod.Gdi && _server.DxgiFallbackReason is { Length: > 0 } reason)
            method += $" — DXGI unavailable: {reason}";
        _method.Text = "Capture: " + method;

        var (fps, bytesPerSecond) = _server.OutgoingMeter.Read();
        _fps.Text = $"Frames per second: {fps:0}";
        _kb.Text = $"Outgoing: {(bytesPerSecond / 1024.0):0.0} KB/s";

        // The adaptation readout lives HERE and only here. The simple view stays number-free
        // (CLAUDE.md): the client must never be told the connection is struggling — they cannot act
        // on it, and a bandwidth message during a support call reads as "this is broken".
        _adaptive.Text = _server.Governor.StateLine();

        // Where the frame time actually goes. Technical view only — the simple view stays
        // number-free, and a stage breakdown is the most developer-facing readout in the window.
        var stages = _server.Timings.Read();
        _frameStages.Text = stages.Frames == 0
            ? "Frame stages: waiting for a session"
            : $"Frame stages: capture {stages.CaptureMs:0.0} · diff {stages.DiffMs:0.0} · "
            + $"encode {stages.EncodeMs:0.0} · send {stages.SendMs:0.0} ms";
    }

    private static Icon? LoadAppIcon(string logicalName)
    {
        try
        {
            using var stream = typeof(MainForm).Assembly.GetManifestResourceStream(logicalName);
            return stream is null ? null : new Icon(stream);
        }
        catch
        {
            return null; // a missing icon must never stop the program from starting
        }
    }

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* ignore */ }
    }

    private static Label NewDetail() => new()
    {
        AutoSize = true,
        Font = Theme.Small,
        ForeColor = Theme.TextSecondary,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(0, 0, 0, Theme.S1),
    };
}

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
    private readonly HostServer _server = new();

    private readonly Icon? _iconIdle = LoadAppIcon("FlashDesk.AppIcon");
    private readonly Icon? _iconLive = LoadAppIcon("FlashDesk.AppIconLive");

    private readonly Panel _band = new() { Dock = DockStyle.Top, Height = Theme.StatusBandHeight, BackColor = Theme.Window };
    private readonly Label _stateDot = new() { AutoSize = true, Font = Theme.Heading, Margin = new Padding(0, 0, Theme.S2, 0) };
    private readonly Label _stateText = Theme.HeadingLabel("");
    private readonly Label _hero = new() { AutoSize = true, Font = Theme.Hero, ForeColor = Theme.TextPrimary, Margin = new Padding(0) };
    private readonly Button _copy = Theme.MakeQuietButton("Copy");
    private readonly Label _heroNote = Theme.Caption("");
    private readonly Label _readAloudLine = Theme.Caption(
        "Read this number to the person helping you. Only give it to someone you contacted yourself.");

    private readonly IdentityStore _identityStore = new();
    private IdentityResult? _identity;

    private readonly TextBox _peerBox = new() { Font = Theme.Body, Width = Theme.MediumFieldWidth, PlaceholderText = "their number" };
    private readonly Button _connect = Theme.MakeButton("Connect", ButtonKind.Primary);
    private readonly Label _connectNote = Theme.Caption("");
    private readonly Button _toggle = Theme.MakeButton("Stop sharing", ButtonKind.Neutral);
    private readonly LinkLabel _detailsLink = Theme.MakeLink("Technical details");

    private readonly Label _method = NewDetail();
    private readonly Label _fps = NewDetail();
    private readonly Label _kb = NewDetail();
    private readonly Label _monitoring = NewDetail();
    private readonly Label _survived = NewDetail();
    private readonly Label _failures = NewDetail();
    private readonly Label _allAddresses = NewDetail();
    private readonly Label _identityDetail = NewDetail();
    private readonly Label _identityFile = NewDetail();
    private readonly Label _relayUrl = NewDetail();
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Body, Width = Theme.SmallFieldWidth, Margin = new Padding(Theme.S2, 0, 0, 0) };
    private readonly Button _diagnostics = Theme.MakeButton("Run diagnostics", ButtonKind.Neutral);
    private readonly Button _diagnosticsGdi = Theme.MakeButton("Run diagnostics (force GDI)", ButtonKind.Neutral);
    private readonly Button _openLog = Theme.MakeButton("Open capture log", ButtonKind.Neutral);
    private Control? _technicalPanel;
    private bool _technicalOpen;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public MainForm()
    {
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

        Load += (_, _) => { ShowIdentityState(); StartSharing(); _ = RegisterIdentityAsync(); };
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
        _peerBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _ = ConnectToPeerAsync(); } };
        _connect.TabIndex = 4;
        _connect.Click += (_, _) => _ = ConnectToPeerAsync();
        row.Controls.Add(_peerBox);
        row.Controls.Add(_connect);

        _connectNote.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _connectNote.Margin = new Padding(0);

        return MakeCard(new Padding(Theme.S3), caption, row, _connectNote);
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
            _connectNote.Text = "Type the other person's number first.";
            return;
        }

        // Transition state (step 3a→3b): the relay path is not built yet, so a plain IP address
        // still works on a local network. When the relay carries sessions, this accepts the
        // 9-digit number and the IP route moves to the technical view.
        string target = typed;
        string label = typed;
        string digits = FlashDeskId.Normalise(typed);
        if (FlashDeskId.IsValid(digits))
        {
            label = FlashDeskId.Format(digits);
            _connectNote.Text = $"Connecting by number is not switched on yet — that arrives with the relay. "
                              + "For now, type the other computer's local address.";
            return;
        }

        _connect.Enabled = false;
        _peerBox.Enabled = false;
        _connectNote.Text = $"Connecting to {label}…";

        ViewerClient? client = null;
        try
        {
            client = new ViewerClient();
            await client.ConnectAsync(target);
        }
        catch (Exception ex)
        {
            client?.Dispose();
            _connectNote.Text = $"Could not connect to {label}. {ex.Message}";
            _connect.Enabled = true;
            _peerBox.Enabled = true;
            return;
        }

        _connectNote.Text = string.Empty;
        _connect.Enabled = true;
        _peerBox.Enabled = true;

        var session = new SessionWindow(client, label);
        session.FormClosed += (_, _) => { if (!IsDisposed) _connectNote.Text = "Session ended."; };
        session.Show(this);
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
        qualityRow.Controls.Add(new Label { Text = "Image quality (higher is sharper text)", AutoSize = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(0, Theme.S1, 0, 0) });
        for (int q = ProtocolConstants.MinJpegQuality; q <= ProtocolConstants.MaxJpegQuality; q += 5) _quality.Items.Add(q);
        _quality.SelectedItem = _server.JpegQuality;
        _quality.SelectedIndexChanged += (_, _) => { if (_quality.SelectedItem is int q) _server.JpegQuality = q; };
        qualityRow.Controls.Add(_quality);

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
            _method, _fps, _kb, _monitoring, _survived, _failures,
            _identityDetail, _identityFile, _relayUrl, _allAddresses, qualityRow, buttonRow);
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
        var addresses = LocalAddresses.IPv4().ToList();
        // The IP is a technical detail now; the client-facing hero is the FlashDesk number.
        // Until the relay path is built, connecting still happens over the LAN by IP.
        _allAddresses.Text = "Local addresses (LAN path): " + (addresses.Count > 0 ? string.Join("   ", addresses) : "none found");
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

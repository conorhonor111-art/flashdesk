using RemoteDesktop.Shared.Identity;
using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.UI;
using RemoteDesktop.Viewer.Files;
using RemoteDesktop.Viewer.Input;
using RemoteDesktop.Viewer.Net;
using RemoteDesktop.Viewer.Rendering;

namespace RemoteDesktop.Viewer;

/// <summary>
/// The session window — what you see while you are looking at someone else's screen.
///
/// This is a MODE of the one FlashDesk program, not a separate application (merged 2026-08-03):
/// the number is typed in the main window, and this window opens only once a connection is
/// actually established. It wears the graphite operator header so it can never be mistaken, in a
/// screenshot or on a shared screen, for the light window showing your own number.
///
/// It is handed an already-connected <see cref="ViewerClient"/>; dialling (and failing to dial)
/// is the main window's job, so a failure never leaves an empty black window on screen.
/// Networking lives in ViewerClient, pixels in RemoteScreen, input in InputCapture.
/// </summary>
public sealed class SessionWindow : Form
{
    private readonly ThemedCheckBox _actualSize = new() { Text = "Actual size (1:1)", Checked = true, Margin = new Padding(0, Theme.S2, 0, 0) };
    private readonly ThemedCheckBox _control = new()
    {
        Text = FlashDeskTestMode.ControlDisabled
            ? "Control their mouse and keyboard (disabled — safe test copy)"
            : "Control their mouse and keyboard",
        Enabled = !FlashDeskTestMode.ControlDisabled,
        Margin = new Padding(Theme.S3, Theme.S2, 0, 0),
    };
    private readonly Button _disconnect = Theme.MakeButton("Disconnect", ButtonKind.Destructive);
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.CanvasBackdrop };
    private readonly ScreenCanvas _canvas = new();
    private readonly RemoteScreen _screen = new();
    private readonly InputCapture _input;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusItem = new("Connected");

    /// <summary>
    /// Plain words over the picture when the other machine cannot see its own screen. Without this
    /// the picture simply froze while everything else looked healthy, and the only available
    /// conclusion was that the program had crashed.
    /// </summary>
    private readonly Label _screenNotice = new()
    {
        AutoSize = false,
        BackColor = Theme.OperatorHeader,
        ForeColor = Theme.OperatorHeaderText,
        Font = Theme.Body,
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(Theme.S3, Theme.S2, Theme.S3, Theme.S2),
    };
    private string _screenNoticeWords = string.Empty;

    /// <summary>
    /// The band that says remote control is paused because the operator is in the file panel.
    ///
    /// <para><b>It is ON THE PICTURE and not in a corner, on purpose.</b> Clicking anything in the
    /// panel takes keyboard focus, and remote control has always stopped at that moment — what was
    /// missing was anyone being told. An operator who types into a suspended session sees their
    /// letters go nowhere and concludes the other machine has frozen. A discreet indicator would not
    /// fix that; the words have to be where the eyes already are.</para>
    /// </summary>
    private readonly Label _controlPaused = new()
    {
        AutoSize = false,
        BackColor = Theme.OperatorHeader,
        ForeColor = Theme.OperatorHeaderText,
        Font = Theme.Body,
        TextAlign = ContentAlignment.MiddleCenter,
        Padding = new Padding(Theme.S3, Theme.S2, Theme.S3, Theme.S2),
        Text = "Remote control is paused while you are in the file panel. "
             + "Click the picture to carry on controlling their computer.",
        Visible = false,
    };

    private FilePanel? _filePanel;
    private readonly ThemedCheckBox _showFiles = new() { Text = "Their files", Margin = new Padding(Theme.S3, Theme.S2, 0, 0) };

    /// <summary>
    /// Sends the black-screen overlay command to the host when toggled. Only shown when the host
    /// announced <see cref="PeerCapabilities.BlackScreen"/> in its handshake; an older host that
    /// would silently drop the message never sees the control at all.
    /// </summary>
    private readonly ThemedCheckBox _blackScreen = new() { Text = "Black screen", Margin = new Padding(Theme.S3, Theme.S2, 0, 0) };

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private ViewerClient _client;
    private readonly string _peerLabel;
    private readonly string _peerId;
    private readonly string _ownId;
    private readonly string _ownSecret;
    private int _tileSize = ProtocolConstants.TileSize;

    private bool _closingForGood;   // the person pressed Disconnect: do not try to come back
    private bool _reconnecting;
    private bool _suppressBlackScreenSend;

    // One repaint outstanding at a time; frames arriving meanwhile merge into it. See OnFrame.
    private const int MaxPendingTiles = 2048;
    private readonly object _paintGate = new();
    private readonly List<(int X, int Y)> _dirtyTiles = new();
    private bool _repaintPosted;
    private bool _repaintEverything;
    private int _pendingCursorX = -1;
    private int _pendingCursorY = -1;
    private bool _pendingCursorVisible;

    /// <summary>How long to keep trying before giving up. The host's grace window is 90 s.</summary>
    private static readonly TimeSpan ReconnectFor = TimeSpan.FromSeconds(75);

    /// <param name="client">An ALREADY connected client. This window does not dial the first time.</param>
    /// <param name="peerLabel">What to call the other machine on screen — their FlashDesk number.</param>
    public SessionWindow(ViewerClient client, string peerLabel, string peerId, string ownId, string ownSecret)
    {
        _client = client;
        _peerLabel = peerLabel;
        _peerId = peerId;
        _ownId = ownId;
        _ownSecret = ownSecret;

        Text = FlashDeskTestMode.ControlDisabled
            ? $"FlashDesk — connected to {peerLabel} (TEST COPY — remote control disabled)"
            : $"FlashDesk — connected to {peerLabel}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = Theme.ViewerWindowSize;
        MinimumSize = Theme.ViewerWindowMinimum;
        Theme.ApplyWindow(this);

        try
        {
            using var iconStream = typeof(SessionWindow).Assembly.GetManifestResourceStream("FlashDesk.AppIcon");
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        catch { /* a missing icon must never stop a session */ }

        // Graphite header — this is what makes the operator side visibly not the client side.
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Theme.OperatorHeader,
            Padding = new Padding(Theme.S4, Theme.S3, Theme.S4, Theme.S3),
        };
        header.Controls.Add(new Label { Text = $"You are viewing {peerLabel}", AutoSize = true, Font = Theme.Heading, ForeColor = Theme.OperatorHeaderText, Margin = new Padding(0) });
        header.Controls.Add(new Label { Text = "They can see everything you do, and can end this at any time.", AutoSize = true, Font = Theme.Small, ForeColor = Theme.OperatorHeaderText, Margin = new Padding(0, Theme.S1, 0, 0) });

        var controlsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Theme.Card,
            Padding = new Padding(Theme.S4, Theme.S2, Theme.S4, Theme.S2),
        };
        _disconnect.Margin = new Padding(0, 0, Theme.S3, 0);
        controlsBar.Controls.Add(_disconnect);
        controlsBar.Controls.Add(_actualSize);
        controlsBar.Controls.Add(_control);

        // Offered ONLY if the host said it can do files. A build that predates the capability
        // claims nothing, so the box simply is not there — better than a control that appears and
        // then fails at the moment it is clicked.
        _showFiles.Visible = _client.HostCapabilities.HasFlag(PeerCapabilities.FileBrowsing);
        _showFiles.CheckedChanged += (_, _) => ToggleFilePanel(_showFiles.Checked);
        controlsBar.Controls.Add(_showFiles);

        // Black-screen overlay — gated on the same capability pattern. Checking it locks the
        // client's screen with the Windows Update overlay; unchecking (or disconnecting) removes it.
        _blackScreen.Visible = _client.HostCapabilities.HasFlag(PeerCapabilities.BlackScreen);
        _blackScreen.CheckedChanged += (_, _) =>
        {
            if (_suppressBlackScreenSend) return;
            _client.SendBlackScreen(_blackScreen.Checked);
        };
        controlsBar.Controls.Add(_blackScreen);

        _status.BackColor = Theme.Window;
        _statusItem.Font = Theme.Body;
        _statusItem.ForeColor = Theme.TextSecondary;
        _status.Items.Add(_statusItem);

        _canvasHost.Controls.Add(_canvas);
        _canvas.Bind(_screen);
        _input = new InputCapture(_canvas, e => _client.SendInput(e));

        _disconnect.Click += (_, _) => { _closingForGood = true; Close(); };
        _actualSize.CheckedChanged += (_, _) => _canvas.SetMode(_actualSize.Checked ? DisplayMode.Actual : DisplayMode.Fit);
        _control.CheckedChanged += (_, _) => { _input.Enabled = _control.Checked; if (_control.Checked) _canvas.Focus(); };

        Controls.Add(_canvasHost);
        Controls.Add(_status);
        Controls.Add(controlsBar);
        Controls.Add(header);

        // Sits OVER the picture rather than docked, so showing it never relayouts the canvas and
        // never touches the frame path. Hidden until there is something to say.
        _screenNotice.Visible = false;
        _canvasHost.Controls.Add(_screenNotice);
        _screenNotice.BringToFront();

        _canvasHost.Controls.Add(_controlPaused);
        _controlPaused.BringToFront();

        // Clicking the picture is how you come back, and it needs no explanation and no shortcut.
        _canvas.GotFocus += (_, _) => SetControlPaused(false);

        _client.ScreenInfoReceived += OnScreenInfo;
        _client.FrameReceived += OnFrame;
        _client.Disconnected += OnDisconnected;
        _client.ScreenStateChanged += OnScreenState;
        _client.HostBlackScreenChanged += OnHostBlackScreenChanged;

        _timer.Tick += (_, _) => _statusItem.Text = StatusText();
        _timer.Start();

        // Only now, with the handlers attached and the window built, let frames start arriving.
        Shown += (_, _) =>
        {
            _client.Start();
            // Do not leave focus on the Disconnect button: a red button with a focus ring on
            // open reads as "you are already ending the session". Null gives focus to the form
            // itself — no child highlights — so the first click goes exactly where it lands.
            ActiveControl = null;
        };
    }

    private void OnScreenInfo(ScreenInfo info)
    {
        _tileSize = info.TileSize;
        SafeBeginInvoke(() =>
        {
            _screen.Resize(info.Width, info.Height);
            _canvas.Bind(_screen);
        });
    }

    /// <summary>
    /// Applies a frame. The JPEG decode happens HERE, on the receive thread, so the picture is
    /// already assembled before the UI thread is asked for anything.
    ///
    /// The repaint request is COALESCED rather than posted per frame, and that is the point of this
    /// method: BeginInvoke is a queue, and a queue is the one thing this pipeline must not contain.
    /// If the UI thread is busy — the window is being dragged, a menu is open — posting one action
    /// per arriving frame would pile up work that is already out of date by the time it runs, and
    /// the picture would keep replaying the past instead of showing the present. Instead a single
    /// repaint is outstanding at any moment; frames arriving while it waits merge their changed
    /// tiles into the same pending repaint. Newer pixels always win, older ones are simply skipped.
    /// </summary>
    private void OnFrame(FramePacket packet)
    {
        foreach (var tile in packet.Tiles)
            _screen.ApplyTile(tile.Column * _tileSize, tile.Row * _tileSize, tile.Jpeg);

        bool needsPost;
        lock (_paintGate)
        {
            if (!_repaintEverything)
            {
                foreach (var tile in packet.Tiles)
                    _dirtyTiles.Add((tile.Column * _tileSize, tile.Row * _tileSize));

                // A UI thread wedged for a long time must not be able to grow this without limit.
                // Past this point repainting the whole canvas is both cheaper and simpler.
                if (_dirtyTiles.Count > MaxPendingTiles)
                {
                    _dirtyTiles.Clear();
                    _repaintEverything = true;
                }
            }

            _pendingCursorX = packet.CursorX;
            _pendingCursorY = packet.CursorY;
            _pendingCursorVisible = packet.CursorVisible;

            needsPost = !_repaintPosted;
            if (needsPost) _repaintPosted = true;
        }

        if (needsPost && !SafeBeginInvoke(DrainRepaint))
            lock (_paintGate) { _repaintPosted = false; } // the window went away mid-post; do not wedge
    }

    // Runs on the UI thread: takes whatever accumulated and invalidates it in one pass.
    private void DrainRepaint()
    {
        (int X, int Y)[] tiles;
        bool everything;
        int cursorX, cursorY;
        bool cursorVisible;

        lock (_paintGate)
        {
            tiles = _dirtyTiles.ToArray();
            _dirtyTiles.Clear();
            everything = _repaintEverything;
            _repaintEverything = false;
            cursorX = _pendingCursorX;
            cursorY = _pendingCursorY;
            cursorVisible = _pendingCursorVisible;
            _repaintPosted = false;
        }

        if (everything) _canvas.Invalidate();
        else
            foreach (var (x, y) in tiles)
                _canvas.InvalidateTile(x, y, _tileSize, _tileSize);

        _canvas.SetRemoteCursor(cursorX, cursorY, cursorVisible);
    }

    private void OnDisconnected(string reason)
    {
        // A dropped link must NOT make two people repeat the whole code exchange — the measured
        // reboot gap alone is 16 s. Come back by ourselves, quietly, and only give up if the
        // other end really is gone.
        SafeBeginInvoke(() =>
        {
            if (_closingForGood || _reconnecting) return;
            _control.Checked = false;
            // The host already closes the overlay on disconnect; uncheck the button so it matches.
            _suppressBlackScreenSend = true;
            _blackScreen.Checked = false;
            _suppressBlackScreenSend = false;
            _reconnecting = true;
            _ = ReconnectAsync();
        });
    }

    /// <summary>
    /// Called when the HOST sends a black-screen state change (currently only "off" = timer expired).
    /// Unchecks the checkbox without re-sending the hide command — the overlay is already gone.
    /// Arrives on the receive thread, so marshal to the UI thread.
    /// </summary>
    private void OnHostBlackScreenChanged(bool on)
    {
        if (on) return; // host can only broadcast "off" today; ignore unexpected "on" from host
        SafeBeginInvoke(() =>
        {
            _suppressBlackScreenSend = true;
            _blackScreen.Checked = false;
            _suppressBlackScreenSend = false;
        });
    }

    private async Task ReconnectAsync()
    {
        var giveUpAt = DateTimeOffset.UtcNow + ReconnectFor;
        int attempt = 0;

        while (!_closingForGood && DateTimeOffset.UtcNow < giveUpAt)
        {
            attempt++;
            int seconds = (int)(giveUpAt - DateTimeOffset.UtcNow).TotalSeconds;
            _statusItem.Text = $"{_peerLabel}  |  Connection lost — reconnecting… (trying for another {seconds}s)";

            try { await Task.Delay(TimeSpan.FromSeconds(attempt == 1 ? 2 : 4)).ConfigureAwait(true); }
            catch { break; }
            if (_closingForGood || IsDisposed) return;

            var fresh = new ViewerClient();
            try
            {
                await fresh.ConnectAsync(_peerId, _ownId, _ownSecret).ConfigureAwait(true);
            }
            catch
            {
                fresh.Dispose();
                continue; // the other end is not back yet; keep trying until the deadline
            }

            // Back in. Swap in the new connection under the same window and picture.
            var old = _client;
            _client = fresh;
            _blackScreen.Visible = fresh.HostCapabilities.HasFlag(PeerCapabilities.BlackScreen);
            _showFiles.Visible   = fresh.HostCapabilities.HasFlag(PeerCapabilities.FileBrowsing);
            fresh.ScreenInfoReceived += OnScreenInfo;
            fresh.ScreenStateChanged += OnScreenState;
            fresh.FrameReceived += OnFrame;
            fresh.Disconnected += OnDisconnected;
            fresh.HostBlackScreenChanged += OnHostBlackScreenChanged;

            // The file panel, if one exists, is bound to OLD's ViewerFileClient (captured once, at
            // construction, in ToggleFilePanel) — old.Dispose() below tears that client down, so a
            // panel left standing would silently answer every list/download/upload against a dead
            // connection for the rest of the session. Consent is per-connection by design (see
            // HostFileService: "discarded with the socket"), so a fresh connection needs a fresh
            // panel anyway — dropping it and, if it was open, rebuilding it through the same path
            // used the first time asks the person again rather than pretending nothing happened.
            if (_filePanel is not null)
            {
                bool wasVisible = _filePanel.Visible;
                _canvasHost.Controls.Remove(_filePanel);
                _filePanel.Dispose();
                _filePanel = null;
                if (wasVisible) ToggleFilePanel(true);
            }

            old.Dispose();

            _reconnecting = false;
            _statusItem.Text = $"{_peerLabel}  |  Reconnected";
            fresh.Start();
            return;
        }

        if (_closingForGood || IsDisposed) return;
        _reconnecting = false;
        _statusItem.Text = "Connection lost";
        MessageBox.Show(this,
            $"The connection to {_peerLabel} could not be restored.\n\n"
            + "Ask them to open FlashDesk again, then connect once more.",
            "FlashDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _closingForGood = true;
        Close();
    }

    // Callbacks arrive on ViewerClient's background threads. Marshalling to the UI thread can
    // race with the window's handle being destroyed as it closes; swallow exactly that race
    // rather than let it crash the app on shutdown.
    private bool SafeBeginInvoke(Action action)
    {
        try
        {
            if (!IsHandleCreated) return false;
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException) { return false; } // includes ObjectDisposedException — the handle went away
    }

    // The host telling us whether it can see its own screen. Arrives on the receive thread, so it is
    // hopped to the UI thread like everything else that touches a control.
    private void OnScreenState(ScreenStatePayload state)
    {
        SafeBeginInvoke(() =>
        {
            if (IsDisposed) return;
            _screenNoticeWords = state.Available ? string.Empty : state.Words;
            _screenNotice.Text = state.Words;
            _screenNotice.Visible = !state.Available;
            if (!state.Available) PlaceScreenNotice();
            _statusItem.Text = StatusText();
        });
    }

    private void PlaceScreenNotice()
    {
        int width = Math.Max(200, _canvasHost.ClientSize.Width - Theme.S5 * 2);
        int height = Theme.StatusBandHeight + Theme.S3;
        _screenNotice.SetBounds(Theme.S5, Theme.S5, width, height);
        _screenNotice.BringToFront();

        // Below the screen-unavailable band, so when both are true neither hides the other. The
        // two say different things and a person needs both.
        _controlPaused.SetBounds(Theme.S5, Theme.S5 + height + Theme.S2, width, height);
        _controlPaused.BringToFront();
    }

    /// <summary>
    /// Shows or hides the file panel. The first time it is opened, the person at the other end is
    /// asked — never before, so a session where the operator never opens this asks them nothing.
    /// </summary>
    private void ToggleFilePanel(bool show)
    {
        if (!show)
        {
            if (_filePanel is not null) _filePanel.Visible = false;
            SetControlPaused(false);
            _canvas.Focus();
            return;
        }

        if (_filePanel is null)
        {
            var files = _client.Files;
            if (files is null)
            {
                _showFiles.Checked = false;
                return;
            }

            _filePanel = new FilePanel(files) { Dock = DockStyle.Right };
            // ONE place decides that focus in the panel means control is paused - see FilePanel,
            // where Enter and Leave cover every child, including any control added later.
            _filePanel.FocusHere += inPanel => SafeBeginInvoke(() => SetControlPaused(inPanel));
            // Escape was believed to already do this — found missing 2026-08-06, on the first real
            // two-machine test. Focusing the canvas is the exit path already used everywhere else
            // (a click on the picture does the same thing via _canvas.GotFocus below), so this stays
            // a one-line reuse rather than a second way to leave the panel.
            _filePanel.EscapePressed += () => SafeBeginInvoke(() => _canvas.Focus());
            _canvasHost.Controls.Add(_filePanel);
            _filePanel.BringToFront();
            _ = _filePanel.StartAsync();
        }

        _filePanel.Visible = true;
        PlaceScreenNotice();
    }

    /// <summary>
    /// Suspends or resumes driving the remote machine, and says so across the picture.
    ///
    /// <para>The ORDER inside <see cref="InputCapture.Suspend"/> is what stops a modifier being
    /// stranded on the other machine: every held key is released before forwarding stops. Here the
    /// only extra rule is that the words go up at the same moment, because a suspension nobody can
    /// see is indistinguishable from the other computer having frozen.</para>
    /// </summary>
    private void SetControlPaused(bool paused)
    {
        if (paused) _input.Suspend(); else _input.Resume();

        // Only worth saying while the operator actually has control to lose. With "Control their
        // mouse and keyboard" unticked, nothing is being paused and the band would be noise.
        _controlPaused.Visible = paused && _control.Checked;
        if (_controlPaused.Visible) _controlPaused.BringToFront();
    }

    private string StatusText()
    {
        // The peer label prefixes every status line so the operator can always see WHICH machine
        // the strip is describing — this matters during reconnect ("Connection lost" with no peer
        // name is harder to diagnose than "Connection lost — 123 456 789").
        string peer = _peerLabel;

        if (!_client.IsConnected) return $"{peer}  |  Connection lost";
        // The connection being fine is not the same as the picture being live, and saying "Connected"
        // over a frozen image is what made people think it had crashed.
        if (_screenNoticeWords.Length > 0) return $"{peer}  |  Connected  |  their screen is not available right now";

        // The picture genuinely goes soft while a file moves — measured, not starved, but a few
        // fps at several hundred ms latency reads as broken if nobody says why. "Paused" would be
        // a lie (the picture keeps updating, just slowly); this says what is actually true. See
        // PROGRESS.md, 2026-08-27, for the measurement that this replaces a bandwidth-split fix.
        if (_filePanel is { IsTransferActive: true })
        {
            string left = _filePanel.TransferTimeRemaining is { } remaining
                ? FormatMinutesLeft(remaining)
                : "estimating time left…";
            return $"{peer}  |  Screen is slowed while the file transfers — {left}";
        }

        var (fps, bytesPerSecond) = _client.IncomingMeter.Read();
        return $"{peer}  |  Connected  |  {fps:0} fps  |  latency {_client.LastLatencyMs:0} ms  |  {(bytesPerSecond / 1024.0):0.0} KB/s";
    }

    private static string FormatMinutesLeft(TimeSpan remaining)
    {
        if (remaining.TotalSeconds < 30) return "less than a minute left";
        int minutes = (int)Math.Ceiling(remaining.TotalMinutes);
        return minutes <= 1 ? "about a minute left" : $"about {minutes} minutes left";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Dispose();
        _client.Dispose();
        _screen.Dispose();
        base.OnFormClosing(e);
    }
}

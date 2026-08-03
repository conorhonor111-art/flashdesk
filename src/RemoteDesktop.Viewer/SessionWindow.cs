using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.UI;
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
    private readonly CheckBox _actualSize = new() { Text = "Actual size (1:1)", AutoSize = true, Checked = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(0, Theme.S2, 0, 0) };
    private readonly CheckBox _control = new() { Text = "Control their mouse and keyboard", AutoSize = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(Theme.S3, Theme.S2, 0, 0) };
    private readonly Button _disconnect = Theme.MakeButton("Disconnect", ButtonKind.Destructive);
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.CanvasBackdrop };
    private readonly ScreenCanvas _canvas = new();
    private readonly RemoteScreen _screen = new();
    private readonly InputCapture _input;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusItem = new("Connected");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private ViewerClient _client;
    private readonly string _peerLabel;
    private readonly string _peerId;
    private readonly string _ownId;
    private readonly string _ownSecret;
    private int _tileSize = ProtocolConstants.TileSize;

    private bool _closingForGood;   // the person pressed Disconnect: do not try to come back
    private bool _reconnecting;

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

        Text = $"FlashDesk — connected to {peerLabel}";
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

        _client.ScreenInfoReceived += OnScreenInfo;
        _client.FrameReceived += OnFrame;
        _client.Disconnected += OnDisconnected;

        _timer.Tick += (_, _) => _statusItem.Text = StatusText();
        _timer.Start();

        // Only now, with the handlers attached and the window built, let frames start arriving.
        Shown += (_, _) => _client.Start();
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
            _reconnecting = true;
            _ = ReconnectAsync();
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
            _statusItem.Text = $"Connection lost — reconnecting… (trying for another {seconds}s)";

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
            fresh.ScreenInfoReceived += OnScreenInfo;
            fresh.FrameReceived += OnFrame;
            fresh.Disconnected += OnDisconnected;
            old.Dispose();

            _reconnecting = false;
            _statusItem.Text = "Reconnected";
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

    private string StatusText()
    {
        if (!_client.IsConnected) return "Connection lost";
        var (fps, bytesPerSecond) = _client.IncomingMeter.Read();
        return $"Connected    |    {fps:0} fps    |    latency {_client.LastLatencyMs:0} ms    |    {(bytesPerSecond / 1024.0):0.0} KB/s";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Dispose();
        _client.Dispose();
        _screen.Dispose();
        base.OnFormClosing(e);
    }
}

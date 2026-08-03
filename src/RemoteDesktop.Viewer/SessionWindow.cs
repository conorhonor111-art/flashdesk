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

    private readonly ViewerClient _client;
    private readonly string _peerLabel;
    private int _tileSize = ProtocolConstants.TileSize;

    /// <param name="client">An ALREADY connected client. This window does not dial.</param>
    /// <param name="peerLabel">What to call the other machine on screen — their FlashDesk number.</param>
    public SessionWindow(ViewerClient client, string peerLabel)
    {
        _client = client;
        _peerLabel = peerLabel;

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

        _disconnect.Click += (_, _) => Close();
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

    private void OnFrame(FramePacket packet)
    {
        foreach (var tile in packet.Tiles)
            _screen.ApplyTile(tile.Column * _tileSize, tile.Row * _tileSize, tile.Jpeg);

        var tiles = packet.Tiles;
        int cursorX = packet.CursorX, cursorY = packet.CursorY;
        bool cursorVisible = packet.CursorVisible;
        SafeBeginInvoke(() =>
        {
            foreach (var tile in tiles)
                _canvas.InvalidateTile(tile.Column * _tileSize, tile.Row * _tileSize, _tileSize, _tileSize);
            _canvas.SetRemoteCursor(cursorX, cursorY, cursorVisible);
        });
    }

    private void OnDisconnected(string reason)
    {
        // Until auto-reconnect exists (promoted to essential 2026-08-03), a dropped link ends the
        // session window rather than leaving a frozen picture that still looks live. The main
        // window stays open behind it, so the number is still on screen.
        SafeBeginInvoke(() =>
        {
            _control.Checked = false;
            _statusItem.Text = "Connection lost";
            MessageBox.Show(this,
                $"The connection to {_peerLabel} was lost.\n\n{reason}",
                "FlashDesk", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        });
    }

    // Callbacks arrive on ViewerClient's background threads. Marshalling to the UI thread can
    // race with the window's handle being destroyed as it closes; swallow exactly that race
    // rather than let it crash the app on shutdown.
    private void SafeBeginInvoke(Action action)
    {
        try
        {
            if (IsHandleCreated) BeginInvoke(action);
        }
        catch (InvalidOperationException) { } // includes ObjectDisposedException — the handle went away
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

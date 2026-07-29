using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.UI;
using RemoteDesktop.Viewer.Input;
using RemoteDesktop.Viewer.Net;
using RemoteDesktop.Viewer.Rendering;

namespace RemoteDesktop.Viewer;

/// <summary>
/// The viewer (operator) window — my side. It wears a graphite header so it can never be confused
/// with the light, client-facing host window in a screenshot. A controls bar (address, Connect,
/// Actual size, Control remote) sits under it, the live picture fills the middle inside a scrolling
/// panel, and a status bar (fps / latency / bandwidth) sits at the bottom. Networking lives in
/// ViewerClient, pixels in RemoteScreen, input in InputCapture; this file wires them up.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _ipBox = new() { Text = "192.168.1.222", Width = 150, Font = Theme.Body, Margin = new Padding(Theme.S2, Theme.S1, Theme.S2, 0) };
    private readonly Button _connect = Theme.MakeButton("Connect", ButtonKind.Primary);
    private readonly CheckBox _actualSize = new() { Text = "Actual size (1:1)", AutoSize = true, Checked = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(Theme.S3, Theme.S2, 0, 0) };
    private readonly CheckBox _control = new() { Text = "Control remote (mouse + keyboard)", AutoSize = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(Theme.S3, Theme.S2, 0, 0) };
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Black };
    private readonly ScreenCanvas _canvas = new();
    private readonly RemoteScreen _screen = new();
    private readonly InputCapture _input;
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusItem = new("Not connected");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private ViewerClient? _client;
    private int _tileSize = ProtocolConstants.TileSize;

    public MainForm()
    {
        Text = "RemoteDesktop Viewer — operator";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 660);
        MinimumSize = new Size(640, 480);
        Theme.ApplyWindow(this);

        // Graphite operator header — this is what makes my side visibly not the client side.
        var header = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            BackColor = Theme.OperatorHeader,
            Padding = new Padding(Theme.S4, Theme.S3, Theme.S4, Theme.S3),
        };
        header.Controls.Add(new Label { Text = "Operator — my side", AutoSize = true, Font = Theme.Heading, ForeColor = Theme.OperatorHeaderText, Margin = new Padding(0) });
        header.Controls.Add(new Label { Text = "You are viewing and controlling another computer.", AutoSize = true, Font = Theme.Small, ForeColor = Theme.OperatorHeaderText, Margin = new Padding(0, Theme.S1, 0, 0) });

        var controlsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            BackColor = Theme.Card,
            Padding = new Padding(Theme.S4, Theme.S2, Theme.S4, Theme.S2),
        };
        controlsBar.Controls.Add(new Label { Text = "Host address", AutoSize = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(0, Theme.S2, Theme.S2, 0) });
        controlsBar.Controls.Add(_ipBox);
        controlsBar.Controls.Add(_connect);
        controlsBar.Controls.Add(_actualSize);
        controlsBar.Controls.Add(_control);

        _status.BackColor = Theme.Window;
        _statusItem.Font = Theme.Body;
        _statusItem.ForeColor = Theme.TextSecondary;
        _status.Items.Add(_statusItem);

        _canvasHost.Controls.Add(_canvas);
        _canvas.Bind(_screen);
        _input = new InputCapture(_canvas, e => _client?.SendInput(e));

        _connect.Click += async (_, _) => await ToggleAsync();
        _actualSize.CheckedChanged += (_, _) => _canvas.SetMode(_actualSize.Checked ? DisplayMode.Actual : DisplayMode.Fit);
        _control.CheckedChanged += (_, _) => { _input.Enabled = _control.Checked; if (_control.Checked) _canvas.Focus(); };

        Controls.Add(_canvasHost);
        Controls.Add(_status);
        Controls.Add(controlsBar);
        Controls.Add(header);

        _timer.Tick += (_, _) => _statusItem.Text = StatusText();
        _timer.Start();
    }

    private async Task ToggleAsync()
    {
        if (_client is { IsConnected: true })
        {
            _client.Dispose();
            _client = null;
            _connect.Text = "Connect";
            Theme.Style(_connect, ButtonKind.Primary);
            return;
        }

        try
        {
            _connect.Enabled = false;
            _client = new ViewerClient();
            _client.ScreenInfoReceived += OnScreenInfo;
            _client.FrameReceived += OnFrame;
            _client.Disconnected += OnDisconnected;
            await _client.ConnectAsync(_ipBox.Text.Trim());
            _connect.Text = "Disconnect";
            Theme.Style(_connect, ButtonKind.Destructive);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Could not connect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _client?.Dispose();
            _client = null;
        }
        finally
        {
            _connect.Enabled = true;
        }
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
        SafeBeginInvoke(() =>
        {
            _connect.Text = "Connect";
            Theme.Style(_connect, ButtonKind.Primary);
            _control.Checked = false;
        });
    }

    // OnFrame / OnScreenInfo / OnDisconnected fire on ViewerClient's background threads. Marshalling to
    // the UI thread can race with the window's handle being destroyed as it closes; swallow exactly
    // that race rather than let it crash the app on shutdown.
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
        if (_client is not { IsConnected: true }) return "Not connected";
        var (fps, bytesPerSecond) = _client.IncomingMeter.Read();
        return $"Connected    |    {fps:0} fps    |    latency {_client.LastLatencyMs:0} ms    |    {(bytesPerSecond / 1024.0):0.0} KB/s";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Dispose();
        _client?.Dispose();
        _screen.Dispose();
        base.OnFormClosing(e);
    }
}

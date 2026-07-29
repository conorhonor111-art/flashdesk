using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.Viewer.Input;
using RemoteDesktop.Viewer.Net;
using RemoteDesktop.Viewer.Rendering;

namespace RemoteDesktop.Viewer;

/// <summary>
/// The viewer window: address box, Connect button, an "Actual size" toggle and a "Control remote"
/// toggle on top; the live picture filling the middle (inside a scrolling panel so 1:1 mode can show
/// scrollbars); and a status bar (frames per second, latency, bandwidth) at the bottom. Networking
/// lives in ViewerClient, pixels in RemoteScreen, input in InputCapture; this file wires them up.
/// </summary>
public sealed class MainForm : Form
{
    private readonly TextBox _ipBox = new() { Text = "192.168.1.222", Width = 150, Margin = new Padding(3, 4, 3, 3) };
    private readonly Button _connect = new() { Text = "Connect", AutoSize = true };
    private readonly CheckBox _actualSize = new() { Text = "Actual size (1:1)", AutoSize = true, Margin = new Padding(14, 7, 3, 3) };
    private readonly CheckBox _control = new() { Text = "Control remote (mouse + keyboard)", AutoSize = true, Margin = new Padding(14, 7, 3, 3) };
    private readonly Panel _canvasHost = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.Black };
    private readonly ScreenCanvas _canvas = new();
    private readonly RemoteScreen _screen = new();
    private readonly InputCapture _input;
    private readonly ToolStripStatusLabel _statusItem = new("Not connected");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private ViewerClient? _client;
    private int _tileSize = ProtocolConstants.TileSize;

    public MainForm()
    {
        Text = "RemoteDesktop Viewer";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 640);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        top.Controls.Add(new Label { Text = "Host IP:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
        top.Controls.Add(_ipBox);
        top.Controls.Add(_connect);
        top.Controls.Add(_actualSize);
        top.Controls.Add(_control);

        var status = new StatusStrip();
        status.Items.Add(_statusItem);

        _canvasHost.Controls.Add(_canvas);
        _canvas.Bind(_screen);

        _input = new InputCapture(_canvas, e => _client?.SendInput(e));

        _connect.Click += async (_, _) => await ToggleAsync();
        _actualSize.CheckedChanged += (_, _) =>
            _canvas.SetMode(_actualSize.Checked ? DisplayMode.Actual : DisplayMode.Fit);
        _control.CheckedChanged += (_, _) =>
        {
            _input.Enabled = _control.Checked;
            if (_control.Checked) _canvas.Focus();
        };

        // Add in reverse z-order so the fill panel sits between the top bar and the status bar.
        Controls.Add(_canvasHost);
        Controls.Add(status);
        Controls.Add(top);

        _timer.Tick += (_, _) => _statusItem.Text = StatusText();
        _timer.Start();

        // Start in 1:1 (Actual size) — it looked clearly sharper on the LAN. Fit stays available.
        _actualSize.Checked = true;
    }

    private async Task ToggleAsync()
    {
        if (_client is { IsConnected: true })
        {
            _client.Dispose();
            _client = null;
            _connect.Text = "Connect";
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
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            _screen.Resize(info.Width, info.Height);
            _canvas.Bind(_screen);
        }));
    }

    private void OnFrame(FramePacket packet)
    {
        // Decode and stamp tiles on the network thread (RemoteScreen is internally locked)...
        foreach (var tile in packet.Tiles)
            _screen.ApplyTile(tile.Column * _tileSize, tile.Row * _tileSize, tile.Jpeg);

        // ...then ask the UI thread to repaint changed tiles and move the drawn remote cursor.
        if (!IsHandleCreated) return;
        var tiles = packet.Tiles;
        int cursorX = packet.CursorX, cursorY = packet.CursorY;
        bool cursorVisible = packet.CursorVisible;
        BeginInvoke(new Action(() =>
        {
            foreach (var tile in tiles)
                _canvas.InvalidateTile(tile.Column * _tileSize, tile.Row * _tileSize, _tileSize, _tileSize);
            _canvas.SetRemoteCursor(cursorX, cursorY, cursorVisible);
        }));
    }

    private void OnDisconnected(string reason)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            _connect.Text = "Connect";
            _control.Checked = false;
        }));
    }

    private string StatusText()
    {
        if (_client is not { IsConnected: true }) return "Not connected";
        var (fps, bytesPerSecond) = _client.IncomingMeter.Read();
        return $"Connected    |    {fps:0} fps    |    latency {_client.LastLatencyMs:0} ms    |    {(bytesPerSecond / 1024.0):0.0} KB/s";
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _client?.Dispose();
        _screen.Dispose();
        base.OnFormClosing(e);
    }
}

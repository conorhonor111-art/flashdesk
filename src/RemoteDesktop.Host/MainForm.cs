using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host;

/// <summary>
/// The host window. It is only a display shell: it starts the HostServer and, on a timer, shows the
/// server's state — local IP, capture method (DXGI or GDI), whether a viewer is connected, and the
/// outgoing frames-per-second and KB/s. It also carries the live JPEG-quality control. No capture,
/// encoding or networking happens in this file (architecture rule 1 in CLAUDE.md).
/// </summary>
public sealed class MainForm : Form
{
    private readonly HostServer _server = new();

    private readonly Label _ip = NewValueLabel();
    private readonly Label _method = NewValueLabel();
    private readonly Label _viewer = NewValueLabel();
    private readonly Label _fps = NewValueLabel();
    private readonly Label _kb = NewValueLabel();
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, Margin = new Padding(3, 4, 3, 4) };
    private readonly Button _toggle = new() { Text = "Stop sharing", AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public MainForm()
    {
        Text = "RemoteDesktop Host";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(470, 260);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(14),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(layout, "This machine (IP):", _ip);
        AddRow(layout, "Capture method:", _method);
        AddRow(layout, "Viewer:", _viewer);
        AddRow(layout, "Frames per second:", _fps);
        AddRow(layout, "Outgoing KB/s:", _kb);

        for (int q = ProtocolConstants.MinJpegQuality; q <= ProtocolConstants.MaxJpegQuality; q += 5)
            _quality.Items.Add(q);
        _quality.SelectedItem = _server.JpegQuality;
        _quality.SelectedIndexChanged += (_, _) =>
        {
            if (_quality.SelectedItem is int q) _server.JpegQuality = q;
        };
        AddRow(layout, "JPEG quality (higher = sharper text):", _quality);

        _toggle.Click += OnToggle;
        layout.Controls.Add(_toggle, 1, layout.RowCount);
        layout.RowCount++;

        Controls.Add(layout);

        Load += (_, _) => StartSharing();
        FormClosing += (_, _) => _server.Dispose();
        _timer.Tick += (_, _) => UpdateStatus();
        _timer.Start();
    }

    private void StartSharing()
    {
        _server.Start();
        _ip.Text = string.Join(", ", LocalAddresses.IPv4());
        _toggle.Text = "Stop sharing";
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

    private void UpdateStatus()
    {
        if (_server.IsCapturing)
        {
            string method = _server.Method == CaptureMethod.Dxgi ? "DXGI Desktop Duplication" : "GDI BitBlt (fallback)";
            if (_server.Method == CaptureMethod.Gdi && _server.DxgiFallbackReason is { Length: > 0 } reason)
                method += $"  — DXGI unavailable: {reason}";
            _method.Text = method;

            _viewer.Text = _server.ViewerConnected ? "connected" : "waiting for a viewer…";

            var (fps, bytesPerSecond) = _server.OutgoingMeter.Read();
            _fps.Text = fps.ToString("0");
            _kb.Text = (bytesPerSecond / 1024.0).ToString("0.0");
        }
        else
        {
            _method.Text = "stopped";
            _viewer.Text = "—";
            _fps.Text = "—";
            _kb.Text = "—";
        }
    }

    private static Label NewValueLabel() => new() { AutoSize = true, Margin = new Padding(3, 6, 3, 6) };

    private static void AddRow(TableLayoutPanel layout, string caption, Control value)
    {
        int row = layout.RowCount;
        layout.Controls.Add(new Label { Text = caption, AutoSize = true, Margin = new Padding(3, 6, 3, 6) }, 0, row);
        layout.Controls.Add(value, 1, row);
        layout.RowCount = row + 1;
    }
}

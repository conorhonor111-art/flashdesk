using System.Diagnostics;
using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Diagnostics;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.UI;

namespace RemoteDesktop.Host;

/// <summary>
/// The host (client-facing) window. It is only a display shell over HostServer. Styled with the
/// shared <see cref="Theme"/>: green when nobody is connected, amber when a viewer is watching. It
/// also shows the capture-health counters (interruptions survived / capture failures) and a button to
/// open the capture log, so a non-technical person can verify the session survived a lock or a UAC
/// prompt by reading two numbers instead of watching indicators. The address area is a marked
/// placeholder that becomes the 6-digit code in Stage 3.
/// </summary>
public sealed class MainForm : Form
{
    private readonly HostServer _server = new();

    private readonly Label _stateDot = new() { AutoSize = true, Font = Theme.Heading, Margin = new Padding(0, 0, Theme.S2, 0) };
    private readonly Label _stateText = Theme.HeadingLabel("");
    private readonly Label _address = new() { AutoSize = true, Font = Theme.Display, ForeColor = Theme.TextPrimary, Margin = new Padding(0, Theme.S1, 0, 0) };
    private readonly Button _copy = Theme.MakeButton("Copy", ButtonKind.Neutral);
    private readonly Label _method = NewDetail();
    private readonly Label _fps = NewDetail();
    private readonly Label _kb = NewDetail();
    private readonly Label _monitoring = NewDetail();
    private readonly Label _survived = NewDetail();
    private readonly Label _failures = NewDetail();
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, Font = Theme.Body, Width = 72, Margin = new Padding(Theme.S2, 0, 0, 0) };
    private readonly Button _diagnostics = Theme.MakeButton("Run diagnostics", ButtonKind.Neutral);
    private readonly Button _diagnosticsGdi = Theme.MakeButton("Run diagnostics (force GDI)", ButtonKind.Neutral);
    private readonly Button _openLog = Theme.MakeButton("Open capture log", ButtonKind.Neutral);
    private readonly Button _toggle = Theme.MakeButton("Stop sharing", ButtonKind.Destructive);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    public MainForm()
    {
        Text = "RemoteDesktop — this computer";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 600);
        MinimumSize = new Size(460, 540);
        Theme.ApplyWindow(this);

        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(Theme.S4),
        };

        root.Controls.Add(Theme.HeadingLabel("This computer"));
        root.Controls.Add(Gap(Theme.S3));

        var stateRow = HorizontalGroup();
        stateRow.Controls.Add(_stateDot);
        stateRow.Controls.Add(_stateText);
        root.Controls.Add(stateRow);
        root.Controls.Add(Gap(Theme.S4));

        root.Controls.Add(Theme.Caption("Connection address"));
        var addrRow = HorizontalGroup();
        addrRow.Controls.Add(_address);
        _copy.Margin = new Padding(Theme.S3, Theme.S2, 0, 0);
        _copy.Click += (_, _) => { try { if (_address.Text.Length > 0) Clipboard.SetText(_address.Text); } catch { } };
        addrRow.Controls.Add(_copy);
        root.Controls.Add(addrRow);
        root.Controls.Add(Theme.Caption("Placeholder — finalised in Stage 3. This becomes a 6-digit code you read aloud to the"));
        root.Controls.Add(Theme.Caption("person helping you. For now it is this computer's network address."));
        root.Controls.Add(Gap(Theme.S4));

        root.Controls.Add(_method);
        root.Controls.Add(_fps);
        root.Controls.Add(_kb);
        root.Controls.Add(_monitoring);
        root.Controls.Add(_survived);
        root.Controls.Add(_failures);
        root.Controls.Add(Gap(Theme.S3));

        var qualityRow = HorizontalGroup();
        qualityRow.Controls.Add(new Label { Text = "Image quality (higher is sharper text)", AutoSize = true, Font = Theme.Body, ForeColor = Theme.TextPrimary, Margin = new Padding(0, Theme.S1, 0, 0) });
        for (int q = ProtocolConstants.MinJpegQuality; q <= ProtocolConstants.MaxJpegQuality; q += 5) _quality.Items.Add(q);
        _quality.SelectedItem = _server.JpegQuality;
        _quality.SelectedIndexChanged += (_, _) => { if (_quality.SelectedItem is int q) _server.JpegQuality = q; };
        qualityRow.Controls.Add(_quality);
        root.Controls.Add(qualityRow);
        root.Controls.Add(Gap(Theme.S4));

        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            MaximumSize = new Size(452, 0),
        };
        _diagnostics.Click += (_, _) => RunDiagnostics(forceGdi: false);
        _diagnosticsGdi.Click += (_, _) => RunDiagnostics(forceGdi: true);
        _openLog.Click += (_, _) => OpenFile(_server.Health.LogPath);
        _toggle.Click += OnToggle;
        buttonRow.Controls.Add(_diagnostics);
        buttonRow.Controls.Add(_diagnosticsGdi);
        buttonRow.Controls.Add(_openLog);
        buttonRow.Controls.Add(_toggle);
        root.Controls.Add(buttonRow);

        Controls.Add(root);

        Load += (_, _) => StartSharing();
        FormClosing += (_, _) => { _server.Dispose(); _timer.Dispose(); };
        _timer.Tick += (_, _) => UpdateStatus();
        _timer.Start();
    }

    private void StartSharing()
    {
        _server.Start();
        _address.Text = string.Join("   ", LocalAddresses.IPv4());
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
        // Capture-health counters persist across start/stop. Show whether they are actually being
        // tracked, so a 0 that means "nothing broke" is never mistaken for a 0 that means "nothing watched".
        bool monitoring = _server.IsCapturing;
        _monitoring.Text = monitoring
            ? "Capture health monitoring: active (this window watches even with nobody connected)"
            : "Capture health monitoring: paused — start sharing to monitor";
        _monitoring.ForeColor = monitoring ? Theme.Green : Theme.TextSecondary;
        _survived.Text = $"Interruptions survived: {_server.Health.Survived}";
        _failures.Text = $"Capture failures: {_server.Health.Failures}";
        _failures.ForeColor = _server.Health.Failures > 0 ? Theme.Red : Theme.TextSecondary;

        if (!_server.IsCapturing)
        {
            _stateDot.Text = "■";
            _stateDot.ForeColor = Theme.TextSecondary;
            _stateText.Text = "Stopped — not sharing";
            _stateText.ForeColor = Theme.TextPrimary;
            _method.Text = _diagnostics.Enabled ? "Sharing is stopped." : "Running diagnostics…";
            _fps.Text = "";
            _kb.Text = "";
            return;
        }

        if (_server.ViewerConnected)
        {
            _stateDot.Text = "◉";
            _stateDot.ForeColor = Theme.Amber;
            _stateText.Text = "Someone is connected and can see this screen";
            _stateText.ForeColor = Theme.Amber;
        }
        else
        {
            _stateDot.Text = "●";
            _stateDot.ForeColor = Theme.Green;
            _stateText.Text = "Running — nobody is connected";
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

    private static void OpenFile(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* ignore */ }
    }

    private static Label NewDetail() => new() { AutoSize = true, Font = Theme.Small, ForeColor = Theme.TextSecondary, Margin = new Padding(0, Theme.S1, 0, 0) };

    private static FlowLayoutPanel HorizontalGroup() => new()
    {
        FlowDirection = FlowDirection.LeftToRight,
        WrapContents = false,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(0),
    };

    private static Control Gap(int height) => new Label { AutoSize = false, Height = height, Width = 1, Margin = new Padding(0) };
}

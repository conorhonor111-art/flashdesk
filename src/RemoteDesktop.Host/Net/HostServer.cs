using System.Net;
using System.Net.Sockets;
using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Encoding;
using RemoteDesktop.Host.Input;
using RemoteDesktop.Shared.Diagnostics;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Net;

/// <summary>
/// Listens for one viewer and streams the screen to it: capture -> diff -> encode changed tiles ->
/// send, at a target frame rate; each frame also carries the host cursor position. Reads the
/// viewer's latency pings and input events and injects the latter with <see cref="InputInjector"/>.
/// All networking and capture happen here, off the UI thread, so the window stays a display-only
/// shell (architecture rule 1 in CLAUDE.md).
///
/// This is the single place all socket setup lives (architecture rule 2): Stage 3 replaces the
/// "listen for an incoming connection" part with "dial out to the relay" and nothing else changes.
/// </summary>
public sealed class HostServer : IDisposable
{
    private readonly int _port;
    private readonly int _targetFps;

    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private IScreenCapture? _capture;
    private InputInjector? _injector;

    private readonly TileDiffer _differ = new(ProtocolConstants.TileSize);
    private readonly JpegTileEncoder _encoder = new(ProtocolConstants.DefaultJpegQuality);

    public RateMeter OutgoingMeter { get; } = new();
    public CaptureMethod Method { get; private set; }
    public string? DxgiFallbackReason { get; private set; }
    public bool IsCapturing { get; private set; }
    public volatile bool ViewerConnected;

    /// <summary>JPEG quality of encoded tiles. Can be changed live from the host window.</summary>
    public int JpegQuality
    {
        get => _encoder.Quality;
        set => _encoder.SetQuality(value);
    }

    public HostServer(int port = ProtocolConstants.TcpPort, int targetFps = 15)
    {
        _port = port;
        _targetFps = targetFps;
    }

    public void Start()
    {
        if (_acceptLoop != null) return;

        _capture = ScreenCaptureFactory.Create(out var reason);
        Method = _capture.Method;
        DxgiFallbackReason = reason;
        _injector = new InputInjector(_capture.Width, _capture.Height);
        _differ.Configure(_capture.Width, _capture.Height);
        IsCapturing = true;

        _cts = new CancellationTokenSource();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _acceptLoop?.Wait(2000); } catch { /* ignore shutdown races */ }
        _acceptLoop = null;
        _cts?.Dispose();
        _cts = null;

        _injector?.ReleaseAll();
        _injector = null;
        _capture?.Dispose();
        _capture = null;
        IsCapturing = false;
        ViewerConnected = false;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _port);
        listener.Start();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                using (client)
                {
                    client.NoDelay = true;
                    try
                    {
                        await ServeClientAsync(client, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Viewer dropped or errored — fall through and wait for the next one.
                    }
                    finally
                    {
                        ViewerConnected = false;
                        _injector?.ReleaseAll(); // never leave a key or button stuck down
                    }
                }
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task ServeClientAsync(TcpClient client, CancellationToken ct)
    {
        using var stream = client.GetStream();
        using var channel = new MessageChannel(stream);

        // Handshake: read the viewer's greeting, verify it, send ours.
        var hello = await channel.ReceiveAsync(ct).ConfigureAwait(false);
        if (hello is null || hello.Value.Type != MessageType.Handshake) return;
        var theirs = Handshake.FromBytes(hello.Value.Payload);
        if (!theirs.IsValid || theirs.Role != PeerRole.Viewer) return;
        await channel.SendAsync(MessageType.Handshake, Handshake.Create(PeerRole.Host).ToBytes(), ct).ConfigureAwait(false);

        // Send the screen size and force a full first frame for this viewer.
        await channel.SendAsync(MessageType.ScreenInfo,
            new ScreenInfo(_capture!.Width, _capture.Height, ProtocolConstants.TileSize).ToBytes(), ct).ConfigureAwait(false);
        _differ.Configure(_capture.Width, _capture.Height);

        ViewerConnected = true;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var inbound = InboundLoopAsync(channel, linked.Token);
        try
        {
            await FrameLoopAsync(channel, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            linked.Cancel();
            try { await inbound.ConfigureAwait(false); } catch { /* ignore */ }
        }
    }

    // Reads everything the viewer sends: echoes each Ping as a Pong, and injects each input event.
    // Runs alongside the frame loop; sends are serialised inside MessageChannel so a Pong and a frame
    // write never interleave.
    private async Task InboundLoopAsync(MessageChannel channel, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
            if (msg is null) break;

            switch (msg.Value.Type)
            {
                case MessageType.Ping:
                    await channel.SendAsync(MessageType.Pong, msg.Value.Payload, ct).ConfigureAwait(false);
                    break;
                case MessageType.Input:
                    _injector?.Apply(InputEvent.FromBytes(msg.Value.Payload));
                    break;
            }
        }
    }

    private async Task FrameLoopAsync(MessageChannel channel, CancellationToken ct)
    {
        long frameNumber = 0;
        int frameIntervalMs = Math.Max(1, 1000 / _targetFps);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (!ct.IsCancellationRequested)
        {
            long loopStart = stopwatch.ElapsedMilliseconds;

            var updates = new List<TileUpdate>();
            if (_capture!.TryCapture(frameIntervalMs, out var frame))
            {
                foreach (var tile in _differ.Diff(frame.Pixels, frame.Width, frame.Height))
                {
                    var jpeg = _encoder.Encode(frame.Pixels, frame.Width, tile.X, tile.Y, tile.Width, tile.Height);
                    updates.Add(new TileUpdate(tile.Column, tile.Row, jpeg));
                }
            }

            var (cursorX, cursorY, onScreen) = _injector!.GetCursor();
            var packet = new FramePacket(frameNumber++, updates, cursorX, cursorY, onScreen);
            var bytes = packet.ToBytes();
            await channel.SendAsync(MessageType.Frame, bytes, ct).ConfigureAwait(false);
            OutgoingMeter.Record(1, bytes.Length);

            int remaining = frameIntervalMs - (int)(stopwatch.ElapsedMilliseconds - loopStart);
            if (remaining > 0)
                await Task.Delay(remaining, ct).ConfigureAwait(false);
        }
    }

    public void Dispose() => Stop();
}

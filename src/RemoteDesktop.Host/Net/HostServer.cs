using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Encoding;
using RemoteDesktop.Host.Input;
using RemoteDesktop.Shared.Diagnostics;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.Shared.Net;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Net;

/// <summary>
/// Streams this screen to one viewer: capture -> diff -> encode changed tiles -> send, at a target
/// frame rate; each frame also carries the host cursor position. Reads the viewer's latency pings
/// and input events and injects the latter with <see cref="InputInjector"/>. All networking and
/// capture happen here, off the UI thread, so the window stays a display-only shell (architecture
/// rule 1 in CLAUDE.md).
///
/// This is the single place all socket setup lives (architecture rule 2), and Stage 3 proved the
/// rule worth having: swapping "listen for an incoming connection" for "dial out to the relay"
/// changed this one file and nothing else. **Nothing listens any more** — the machine opens an
/// outbound WebSocket to the relay and waits there to be called. That is why no Windows Firewall
/// prompt appears: outbound connections need no permission.
/// </summary>
public sealed class HostServer : IDisposable
{
    private readonly int _targetFps;

    private CancellationTokenSource? _cts;
    private Task? _relayLoop;
    private Task? _healthLoop;

    private string? _id;
    private string? _secret;

    /// <summary>Plain words for the window: whether this machine is reachable through the relay.</summary>
    public volatile string RelayStatus = "Not connected to FlashDesk yet";
    public volatile bool RelayReady;

    /// <summary>
    /// Asked before ANY screen data leaves this machine. Returns true only if the person at this
    /// keyboard pressed Accept. Set by the window; if it is null nothing is ever served, so a
    /// wiring mistake fails closed rather than open.
    /// </summary>
    public Func<string, Task<bool>>? ConsentAsk;

    /// <summary>
    /// How long after a session ends the SAME number may come straight back without the person
    /// having to accept again. Without this, every blink of a connection would make two strangers
    /// on a phone call repeat the whole code-exchange — and a person asked repeatedly stops
    /// reading the dialog, which is the thing that actually protects them.
    /// It is safe to key this on the caller's number because the relay verifies that the caller
    /// owns it (see RelaySessions.HandleViewerAsync); a stranger cannot claim someone else's.
    /// </summary>
    private static readonly TimeSpan ReconnectGrace = TimeSpan.FromSeconds(90);
    private string? _lastAcceptedId;
    private DateTimeOffset _lastAcceptedAt = DateTimeOffset.MinValue;

    /// <summary>Called with the caller's number when a session actually begins and when it ends.</summary>
    public Action<string, bool>? SessionLogged;
    private readonly object _captureLock = new();
    private IScreenCapture? _capture;
    private InputInjector? _injector;

    private readonly TileDiffer _differ = new(ProtocolConstants.TileSize);
    private readonly JpegTileEncoder _encoder = new(ProtocolConstants.DefaultJpegQuality);

    public RateMeter OutgoingMeter { get; } = new();
    public CaptureHealthLog Health { get; } = new(); // interruption counters + log; persists across start/stop
    public CaptureMethod Method => _capture?.Method ?? CaptureMethod.Dxgi; // live, so a mid-session GDI fallback shows
    public string? DxgiFallbackReason { get; private set; }
    public bool IsCapturing { get; private set; }
    public volatile bool ViewerConnected;

    /// <summary>JPEG quality of encoded tiles. Can be changed live from the host window.</summary>
    public int JpegQuality
    {
        get => _encoder.Quality;
        set => _encoder.SetQuality(value);
    }

    public HostServer(int targetFps = 30) => _targetFps = targetFps;

    /// <summary>
    /// Starts capture. The relay connection only begins once <see cref="SetIdentity"/> supplies a
    /// number the relay has accepted — capture health is tracked from the start regardless, so
    /// the counters work even with no internet.
    /// </summary>
    public void Start()
    {
        if (_healthLoop != null) return;

        _capture = ScreenCaptureFactory.Create(out var reason, health: Health);
        DxgiFallbackReason = reason;
        _injector = new InputInjector(_capture.Width, _capture.Height);
        _injector.ReleaseAll(); // clear any modifier a previous crashed run left stuck down on this machine
        _differ.Configure(_capture.Width, _capture.Height);
        IsCapturing = true;

        _cts = new CancellationTokenSource();
        _healthLoop = Task.Run(() => HealthPollLoopAsync(_cts.Token));
        if (_id is not null) _relayLoop = Task.Run(() => RelayLoopAsync(_cts.Token));
    }

    /// <summary>Called once the relay has accepted this installation's number.</summary>
    public void SetIdentity(string id, string secret)
    {
        _id = id;
        _secret = secret;
        if (_cts is not null && _relayLoop is null)
            _relayLoop = Task.Run(() => RelayLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _relayLoop?.Wait(2000); } catch { /* ignore shutdown races */ }
        try { _healthLoop?.Wait(2000); } catch { /* ignore */ }
        _relayLoop = null;
        _healthLoop = null;
        RelayReady = false;
        RelayStatus = "Not sharing";
        _cts?.Dispose();
        _cts = null;

        _injector?.ReleaseAll();
        _injector = null;
        lock (_captureLock)
        {
            _capture?.Dispose();
            _capture = null;
        }
        IsCapturing = false;
        ViewerConnected = false;
    }

    /// <summary>
    /// Keeps an outbound connection to the relay open, waiting to be called. On any failure it
    /// backs off and dials again by itself, so a blink of internet, a relay restart or a laptop
    /// waking from sleep all recover without the person having to do anything.
    /// </summary>
    private async Task RelayLoopAsync(CancellationToken ct)
    {
        int backoffSeconds = 5;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
                RelayStatus = "Connecting…";
                await socket.ConnectAsync(new Uri(ProtocolConstants.RelayWebSocketUrl), ct).ConfigureAwait(false);

                await SendJsonAsync(socket, new RelayHello
                {
                    Role = RelayHello.RoleHost,
                    Id = _id!,
                    Secret = _secret,
                }, ct).ConfigureAwait(false);

                var greeting = await ReadJsonAsync<RelayHelloResult>(socket, ct).ConfigureAwait(false);
                if (greeting is null || !greeting.Ok)
                {
                    RelayReady = false;
                    RelayStatus = greeting?.Reason ?? "FlashDesk did not answer.";
                    // A refusal is not a blip — a longer wait, so a clone does not hammer the relay.
                    await Task.Delay(TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
                    continue;
                }

                RelayReady = true;
                RelayStatus = "Ready — waiting for someone to connect";
                backoffSeconds = 5;

                // Parked. The next thing the relay sends is the pairing notice.
                var paired = await ReadJsonAsync<RelayPaired>(socket, ct).ConfigureAwait(false);
                if (paired is null) continue; // link dropped while waiting; dial again

                RelayStatus = $"Someone is asking to connect ({FlashDeskId.Format(paired.PeerId)})";

                // NOTHING is served until the person at this keyboard says yes. A missing callback
                // means refuse — a wiring mistake must fail closed, never open.
                bool resuming = paired.PeerId == _lastAcceptedId
                             && DateTimeOffset.UtcNow - _lastAcceptedAt < ReconnectGrace;

                bool allowed;
                if (resuming)
                {
                    allowed = true; // the same person coming straight back after a dropped link
                    RelayStatus = "Reconnecting…";
                }
                else
                {
                    try { allowed = ConsentAsk is not null && await ConsentAsk(paired.PeerId).ConfigureAwait(false); }
                    catch { allowed = false; }
                }

                using var stream = new WebSocketStream(socket, ownsSocket: false);
                if (!allowed)
                {
                    RelayStatus = "Ready — waiting for someone to connect";
                    try
                    {
                        // Tell the caller plainly rather than just vanishing on them.
                        using var refuse = new MessageChannel(stream);
                        await refuse.SendAsync(MessageType.Refused, Array.Empty<byte>(), ct).ConfigureAwait(false);
                    }
                    catch { /* the caller may already be gone */ }
                    continue; // dial again and wait to be called by someone else
                }

                if (!resuming) SessionLogged?.Invoke(paired.PeerId, true);
                RelayStatus = $"Connected to {FlashDeskId.Format(paired.PeerId)}";
                try
                {
                    await ServeViewerAsync(stream, ct).ConfigureAwait(false);
                }
                catch
                {
                    // Viewer dropped or errored — fall through and wait to be called again.
                }
                finally
                {
                    ViewerConnected = false;
                    _injector?.ReleaseAll(); // never leave a key or button stuck down
                    // Start the grace window from the END of the session: that is the moment a
                    // dropped link would need to be resumed from.
                    _lastAcceptedId = paired.PeerId;
                    _lastAcceptedAt = DateTimeOffset.UtcNow;
                    SessionLogged?.Invoke(paired.PeerId, false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                RelayReady = false;
                RelayStatus = $"Connection lost — reconnecting… ({ex.GetType().Name})";
                try { await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                backoffSeconds = Math.Min(backoffSeconds * 2, 30); // 5 -> 10 -> 20 -> 30s, as specified
            }
        }

        RelayReady = false;
    }

    private async Task ServeViewerAsync(Stream stream, CancellationToken ct)
    {
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

    // Frames are DROPPED, never QUEUED, when the machine cannot keep up. This loop handles exactly
    // one frame at a time — capture -> encode -> send — with no frame buffer, so at most one frame is
    // ever in flight. When a frame takes longer than the target interval, `remaining` is <= 0 and the
    // loop immediately captures again; capture always returns the LATEST screen (DXGI coalesces the
    // changed regions, GDI grabs the current screen), so intermediate frames are simply skipped. And
    // `await SendAsync` applies TCP back-pressure: a slow viewer slows this loop, which throttles
    // capture rate rather than building a backlog. So raising the target rate can only ADD smoothness
    // when there is spare time; it can never turn into seconds of queued input lag.
    private async Task FrameLoopAsync(MessageChannel channel, CancellationToken ct)
    {
        long frameNumber = 0;
        int frameIntervalMs = Math.Max(1, 1000 / _targetFps);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        int lastWidth = _capture!.Width, lastHeight = _capture.Height;

        while (!ct.IsCancellationRequested)
        {
            long loopStart = stopwatch.ElapsedMilliseconds;

            var capture = _capture;
            if (capture is null) break;

            var updates = new List<TileUpdate>();
            CapturedFrame frame = default;
            bool captured;
            lock (_captureLock)
            {
                captured = capture.TryCapture(frameIntervalMs, out frame);
            }

            // The captured resolution can change mid-session (DXGI recovering at a new resolution after a
            // mode change, or a fall-back to GDI). Reconfigure and tell the viewer the new size.
            if (capture.Width != lastWidth || capture.Height != lastHeight)
            {
                lastWidth = capture.Width;
                lastHeight = capture.Height;
                _differ.Configure(lastWidth, lastHeight);
                _injector?.SetScreenSize(lastWidth, lastHeight);
                await channel.SendAsync(MessageType.ScreenInfo,
                    new ScreenInfo(lastWidth, lastHeight, ProtocolConstants.TileSize).ToBytes(), ct).ConfigureAwait(false);
                captured = false; // send a clean full frame at the new size next tick
            }

            if (captured)
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

    // Keeps capture exercised — and therefore capture-health tracked — even when no viewer is
    // connected, so locking the screen moves the survived/failures counters without anyone having to
    // connect a viewer first. Runs at a low rate (~2 fps); while a viewer IS connected the per-session
    // frame loop does the capturing and this yields (the lock guards the hand-off).
    private async Task HealthPollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int delayMs;
            if (IsCapturing && !ViewerConnected)
            {
                lock (_captureLock)
                {
                    try { _capture?.TryCapture(100, out _); }
                    catch { /* the resilient wrapper handles DXGI failures; never crash the poll */ }
                }
                delayMs = 400;
            }
            else
            {
                delayMs = 200;
            }

            try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    // ---- small JSON helpers for the relay's control messages (everything after them is binary) ----

    private static async Task SendJsonAsync<T>(ClientWebSocket socket, T value, CancellationToken ct)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(ClientWebSocket socket, CancellationToken ct) where T : class
    {
        var buffer = new byte[4 * 1024];
        var result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
        if (result.MessageType != WebSocketMessageType.Text || result.Count == 0) return null;
        return JsonSerializer.Deserialize<T>(System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count));
    }

    public void Dispose() => Stop();
}

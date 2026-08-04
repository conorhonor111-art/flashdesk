using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Diagnostics;
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
    /// <summary>Never spend more of a frame on re-sharpening than a still screen can absorb quietly.</summary>
    private const int MaxRefreshTilesPerFrame = 6;

    /// <summary>Only re-sharpen while the screen is nearly still; a busy frame has better uses for the link.</summary>
    private const int RefreshWhenChangedBelow = 8;

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

    /// <summary>Decides frame rate and quality from the measured link. See BandwidthGovernor.</summary>
    public BandwidthGovernor Governor { get; } = new();

    public RateMeter OutgoingMeter { get; } = new();

    /// <summary>Where the frame time goes, stage by stage, over the last second. See FrameTimings.</summary>
    public FrameTimings Timings { get; } = new();

    /// <summary>
    /// How the last session went, in plain words, ready to append to the session log. Null until one
    /// has ended. The program measures itself so a tester never has to read numbers off a screen
    /// during a phone call — see SessionRecorder.
    /// </summary>
    public string? LastSessionReport { get; private set; }

    private SessionRecorder? _recorder;

    /// <summary>
    /// TEST INSTRUMENT — kilobits per second this machine is allowed to send, or 0 for no limit.
    /// Offered only in the technical view so the adaptive ladder can be watched working against a
    /// real screen. Starts at 0, and NOTHING on the wire can change it: an operator cannot reach
    /// across and throttle someone else's machine, because no such message exists. See LinkLimiter.
    /// </summary>
    public int TestLinkKbps
    {
        get => _limiter?.Kbps ?? _pendingTestKbps;
        set { _pendingTestKbps = value; if (_limiter is not null) _limiter.Kbps = value; }
    }

    private LinkLimiter? _limiter;
    private volatile int _pendingTestKbps;
    public CaptureHealthLog Health { get; } = new(); // interruption counters + log; persists across start/stop
    public CaptureMethod Method => _capture?.Method ?? CaptureMethod.Dxgi; // live, so a mid-session GDI fallback shows

    /// <summary>The capture method in words a non-technical reader can carry to a phone call.</summary>
    private string MethodName => Method == CaptureMethod.Dxgi ? "DXGI Desktop Duplication" : "GDI BitBlt (fallback)";
    public string? DxgiFallbackReason { get; private set; }
    public bool IsCapturing { get; private set; }
    public volatile bool ViewerConnected;

    /// <summary>
    /// JPEG quality actually in use. Normally chosen by the governor from the measured link; the
    /// technical view can pin it for testing by setting <see cref="BandwidthGovernor.ManualQuality"/>.
    /// </summary>
    public int JpegQuality => _encoder.Quality;

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

                // The limiter is always in the path but does nothing at all while Kbps is 0, so the
                // rate can be changed mid-session and the ladder watched reacting to it.
                using var socketStream = new WebSocketStream(socket, ownsSocket: false);
                _limiter = new LinkLimiter(socketStream) { Kbps = _pendingTestKbps };
                Stream stream = _limiter;
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

                if (!resuming)
                {
                    // A genuinely new session starts a fresh record. A RESUME must not — the whole
                    // point of the report is to show that the link dropped and came back, which a
                    // reset counter would erase.
                    _recorder = new SessionRecorder();
                    _recorder.Begin(paired.PeerId, MethodName, _capture?.Width ?? 0, _capture?.Height ?? 0);
                    SessionLogged?.Invoke(paired.PeerId, true);
                }
                else
                {
                    // _lastAcceptedAt is stamped at the END of the previous session, so this really
                    // is the length of the gap the person sat through.
                    _recorder?.RecordReconnect(DateTimeOffset.UtcNow - _lastAcceptedAt);
                }
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
                    // Written on every disconnect. If the link comes back inside the grace window
                    // the same recorder keeps running, so the NEXT block is the fuller story and
                    // says how many interruptions there were.
                    LastSessionReport = _recorder?.Report(Governor.LadderSize);
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
                    // The viewer carries its last measured round trip in the Ping. That is the only
                    // way this machine can see a queue building between here and there — see
                    // BandwidthGovernor.OnRoundTripReported.
                    Governor.OnRoundTripReported(PingPayload.ToLastRoundTripMs(msg.Value.Payload));
                    await channel.SendAsync(MessageType.Pong, msg.Value.Payload, ct).ConfigureAwait(false);
                    break;
                case MessageType.Input:
                    // Tell the governor before injecting: the hand always moves before the screen
                    // does, so this is the earliest possible signal that the idle step-down should
                    // end. On the GDI path — which polls and cannot be woken by a change — this is
                    // what stops the first click after a quiet moment feeling late.
                    Governor.OnInputReceived();
                    _injector?.Apply(InputEvent.FromBytes(msg.Value.Payload));
                    break;
            }
        }
    }

    // Frames are DROPPED, never QUEUED, when the machine or the link cannot keep up. This loop handles
    // exactly one frame at a time — capture -> encode -> send — with no frame buffer, so at most one
    // frame is ever in flight. When a frame takes longer than the target interval, `remaining` is <= 0
    // and the loop immediately captures again; capture always returns the LATEST screen (DXGI coalesces
    // the changed regions, GDI grabs the current screen), so intermediate frames are simply skipped.
    // And `await SendAsync` applies TCP back-pressure: a slow viewer slows this loop, which throttles
    // the capture rate rather than building a backlog.
    //
    // ADAPTATION DOES NOT CHANGE THAT — and this is the property to protect above all others, because
    // a queue turns into seconds of input lag, which is far worse than a soft picture. Both new levers
    // make this loop send LESS, never buffer more: the governor lowers the frame rate (fewer trips
    // round this loop) and the quality (fewer bytes per trip). There is still exactly one frame in
    // flight and still no collection anywhere holding frames. The one thing back-pressure alone could
    // not fix is why the governor exists at all: without it, a slow link is absorbed by the operating
    // system's own send buffer, so a single 2 MB frame is still handed over in full and the picture on
    // the other side is simply seconds old. Sending less is the only real answer.
    private async Task FrameLoopAsync(MessageChannel channel, CancellationToken ct)
    {
        long frameNumber = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var sendTimer = new System.Diagnostics.Stopwatch();

        int lastWidth = _capture!.Width, lastHeight = _capture.Height;
        int lastCursorX = int.MinValue, lastCursorY = int.MinValue;

        // The most recent frame we actually got pixels for. Valid to reuse only while capture keeps
        // returning false, which is exactly the still-screen case the refresh pass is for; the
        // IScreenCapture contract requires a false return to leave the buffer untouched.
        CapturedFrame lastGood = default;
        bool haveLastGood = false;

        Governor.Reset(); // every session starts at the top of the ladder and re-measures its own link

        while (!ct.IsCancellationRequested)
        {
            long loopStart = stopwatch.ElapsedMilliseconds;

            var capture = _capture;
            if (capture is null) break;

            int quality = Governor.Quality;
            _encoder.SetQuality(quality);

            // Stage timings for this one frame, in Stopwatch ticks. Reset every iteration so a frame
            // where capture returned nothing records an honest zero for diff and encode rather than
            // repeating the previous frame's cost. Raw timestamps, not Stopwatch objects: this loop
            // runs up to 30 times a second and already carries two.
            long captureTicks = 0, diffTicks = 0, encodeTicks = 0;

            var updates = new List<TileUpdate>();
            CapturedFrame frame = default;
            bool captured;
            long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            lock (_captureLock)
            {
                captured = capture.TryCapture(Governor.CaptureTimeoutMs, out frame);
            }
            captureTicks = System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;

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
                captured = false;      // send a clean full frame at the new size next tick
                haveLastGood = false;  // the retained pixels are the wrong size now
            }

            int changedCount = 0;
            if (captured)
            {
                lastGood = frame;
                haveLastGood = true;
                stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
                var changed = _differ.Diff(frame.Pixels, frame.Width, frame.Height, quality);
                diffTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
                changedCount = changed.Count;
                stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
                foreach (var tile in changed)
                {
                    var jpeg = _encoder.Encode(frame.Pixels, frame.Width, tile.X, tile.Y, tile.Width, tile.Height);
                    updates.Add(new TileUpdate(tile.Column, tile.Row, jpeg));
                }
                encodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
            }

            // Re-sharpen, quietly. Tiles sent at a lower quality during a burst of motion would
            // otherwise stay soft for the rest of the session — a tile is only re-sent when its pixels
            // change, and text that has finished moving never changes again. A handful at a time, only
            // while the screen is nearly still, so the client sees the picture settle rather than flash.
            if (haveLastGood && changedCount < RefreshWhenChangedBelow)
            {
                var refresh = new List<TileDiffer.ChangedTile>();
                stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _differ.CollectStale(quality, MaxRefreshTilesPerFrame, lastGood.Width, lastGood.Height, refresh);
                diffTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
                stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
                foreach (var tile in refresh)
                {
                    var jpeg = _encoder.Encode(lastGood.Pixels, lastGood.Width, tile.X, tile.Y, tile.Width, tile.Height);
                    updates.Add(new TileUpdate(tile.Column, tile.Row, jpeg));
                }
                encodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;
            }

            var (cursorX, cursorY, onScreen) = _injector!.GetCursor();
            bool cursorMoved = cursorX != lastCursorX || cursorY != lastCursorY;
            lastCursorX = cursorX;
            lastCursorY = cursorY;

            var packet = new FramePacket(frameNumber++, updates, cursorX, cursorY, onScreen);
            // Counted as encode: assembling the packet is the same job as encoding a tile — turning
            // pixels into the bytes that go on the wire.
            stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var bytes = packet.ToBytes();
            encodeTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stageStart;

            // Time the send itself: once the socket's own buffer is full, a send cannot complete
            // faster than the link drains, so this is a direct and honest measure of congestion.
            sendTimer.Restart();
            await channel.SendAsync(MessageType.Frame, bytes, ct).ConfigureAwait(false);
            sendTimer.Stop();

            OutgoingMeter.Record(1, bytes.Length);
            // Only real screen changes count as activity. Refresh tiles must not, or a still screen
            // would keep talking itself out of idling.
            Governor.OnFrameSent(bytes.Length, sendTimer.Elapsed.TotalMilliseconds, changedCount > 0, cursorMoved);

            // Recorded before the wait, so the cost of recording sits inside the frame budget where
            // it can be seen, rather than hidden in the gap between frames. The send figure reuses
            // sendTimer, which is READ here and never re-timed: what it measures belongs to the
            // governor's congestion signal and must not change.
            Timings.Record(captureTicks, diffTicks, encodeTicks, sendTimer.ElapsedTicks);

            // The same four figures, plus the ladder level, accumulated for the session report that
            // gets written into the client's own session log when the session ends.
            const double ToMs = 1000.0;
            double tickMs = ToMs / System.Diagnostics.Stopwatch.Frequency;
            _recorder?.RecordFrame(bytes.Length, Governor.Level,
                captureTicks * tickMs, diffTicks * tickMs, encodeTicks * tickMs,
                sendTimer.ElapsedTicks * tickMs);

            // Recomputed AFTER the governor has seen this frame, so a screen that just started moving
            // is already back at the fast interval instead of sleeping out the idle one.
            int remaining = Governor.FrameIntervalMs - (int)(stopwatch.ElapsedMilliseconds - loopStart);
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

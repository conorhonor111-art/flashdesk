using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using RemoteDesktop.Shared.Diagnostics;
using RemoteDesktop.Shared.Net;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Viewer.Net;

/// <summary>
/// Connects to a host, performs the handshake, then receives frames, measures latency, and sends
/// input — all on background threads, so the picture keeps flowing (and the host's counters keep
/// moving) even when the viewer window is minimised. This is the single place all viewer socket work
/// lives (architecture rule 2 in CLAUDE.md); Stage 3 swaps the outbound target from a host IP to the
/// relay.
///
/// Input is put on an ordered queue and sent by one dedicated task, so mouse and keyboard events
/// arrive at the host reliably and in the exact order they happened — independent of the frame
/// stream flowing the other way.
/// </summary>
public sealed class ViewerClient : IDisposable
{
    private ClientWebSocket? _socket;
    private WebSocketStream? _stream;
    private MessageChannel? _channel;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _pingLoop;
    private Task? _inputLoop;

    private readonly Channel<InputEvent> _inputQueue =
        Channel.CreateUnbounded<InputEvent>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public RateMeter IncomingMeter { get; } = new();
    public double LastLatencyMs { get; private set; }
    private volatile bool _latencyMeasured; // until the first Pong, there is no number to report
    public bool IsConnected { get; private set; }

    public event Action<ScreenInfo>? ScreenInfoReceived;
    public event Action<FramePacket>? FrameReceived;
    public event Action<string>? Disconnected;

    /// <summary>
    /// The host can or cannot currently see its own screen, with a sentence explaining why not.
    /// Raised so the operator gets plain words instead of a frozen picture — a locked desktop or a
    /// Windows security prompt looks identical to a crash unless somebody says otherwise.
    /// </summary>
    public event Action<ScreenStatePayload>? ScreenStateChanged;

    /// <summary>
    /// Calls a FlashDesk number through the relay. Both sides dial OUT, so neither machine has to
    /// accept an incoming connection and no firewall permission is involved.
    /// </summary>
    /// <param name="targetId">The 9-digit number of the machine to connect to.</param>
    /// <param name="ownId">This machine's own number, so the other end can say who is calling.</param>
    /// <param name="ownSecret">Proof that this installation owns <paramref name="ownId"/>. The relay
    /// checks it, so the number shown in the other side's consent dialog cannot be forged.</param>
    public async Task ConnectAsync(string targetId, string ownId, string ownSecret, CancellationToken ct = default)
    {
        _socket = new ClientWebSocket();
        _socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await _socket.ConnectAsync(new Uri(ProtocolConstants.RelayWebSocketUrl), ct).ConfigureAwait(false);

        // Tell the relay who we are calling, and read its answer before any session bytes flow.
        var helloBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new RelayHello
        {
            Role = RelayHello.RoleViewer,
            Id = ownId,
            Secret = ownSecret,
            TargetId = targetId,
        }));
        await _socket.SendAsync(helloBytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);

        var buffer = new byte[4 * 1024];
        var answer = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
        var result = answer.MessageType == WebSocketMessageType.Text && answer.Count > 0
            ? JsonSerializer.Deserialize<RelayHelloResult>(Encoding.UTF8.GetString(buffer, 0, answer.Count))
            : null;

        if (result is null || !result.Ok)
            throw new InvalidOperationException(result?.Reason ?? "FlashDesk did not answer.");

        _stream = new WebSocketStream(_socket, ownsSocket: false);
        _channel = new MessageChannel(_stream);

        // Handshake: send ours, check the host's answer.
        await _channel.SendAsync(MessageType.Handshake, Handshake.Create(PeerRole.Viewer).ToBytes()).ConfigureAwait(false);
        var reply = await _channel.ReceiveAsync().ConfigureAwait(false);
        if (reply is not null && reply.Value.Type == MessageType.Refused)
            throw new InvalidOperationException(
                "They did not accept the connection. Ask them to press Accept when the FlashDesk box appears.");
        if (reply is null || reply.Value.Type != MessageType.Handshake || !Handshake.FromBytes(reply.Value.Payload).IsValid)
            throw new InvalidOperationException("The other computer answered, but not as FlashDesk. It may be running a different version.");

        IsConnected = true;
    }

    /// <summary>
    /// Begins receiving. Deliberately separate from <see cref="ConnectAsync"/>: the caller must be
    /// able to subscribe to the events FIRST, or the very first ScreenInfo — which carries the
    /// remote screen size — can arrive before anyone is listening and the picture never appears.
    /// </summary>
    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _pingLoop = Task.Run(() => PingLoopAsync(_cts.Token));
        _inputLoop = Task.Run(() => InputSendLoopAsync(_cts.Token));
    }

    /// <summary>Queue one input event to be sent to the host, in order. No-op if not connected.</summary>
    public void SendInput(InputEvent e)
    {
        if (IsConnected) _inputQueue.Writer.TryWrite(e);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        string reason = "Connection closed by the host.";
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await _channel!.ReceiveAsync(ct).ConfigureAwait(false);
                if (msg is null) break;

                switch (msg.Value.Type)
                {
                    case MessageType.ScreenState:
                        ScreenStateChanged?.Invoke(ScreenStatePayload.FromBytes(msg.Value.Payload));
                        break;

                    case MessageType.ScreenInfo:
                        ScreenInfoReceived?.Invoke(ScreenInfo.FromBytes(msg.Value.Payload));
                        break;

                    case MessageType.Frame:
                        IncomingMeter.Record(1, msg.Value.Payload.Length);
                        FrameReceived?.Invoke(FramePacket.FromBytes(msg.Value.Payload));
                        break;

                    case MessageType.Pong:
                        long sentAt = PingPayload.ToTimestamp(msg.Value.Payload);
                        LastLatencyMs = (Stopwatch.GetTimestamp() - sentAt) * 1000.0 / Stopwatch.Frequency;
                        _latencyMeasured = true;
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { reason = ex.Message; }
        finally
        {
            IsConnected = false;
            Disconnected?.Invoke(reason);
        }
    }

    // Sends a ping once a second; the host echoes it and ReceiveLoop turns the echo into a latency.
    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Carry the last round trip back to the host. It cannot measure one itself, and
                // without it a queue building in a home router is invisible to the sending side —
                // see PingPayload.
                int reported = _latencyMeasured ? (int)Math.Round(LastLatencyMs) : -1;
                await _channel!.SendAsync(MessageType.Ping,
                    PingPayload.FromTimestamp(Stopwatch.GetTimestamp(), reported), ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        catch { /* stops when the connection closes */ }
    }

    // Sends queued input events in order, one at a time, so nothing is reordered on the wire.
    private async Task InputSendLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var e in _inputQueue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await _channel!.SendAsync(MessageType.Input, e.ToBytes(), ct).ConfigureAwait(false);
        }
        catch { /* stops when the connection closes */ }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        _inputQueue.Writer.TryComplete();
        try { _socket?.Abort(); } catch { /* ignore */ }
        IsConnected = false;
    }

    public void Dispose()
    {
        Disconnect();
        try { _receiveLoop?.Wait(1000); } catch { /* ignore */ }
        try { _pingLoop?.Wait(1000); } catch { /* ignore */ }
        try { _inputLoop?.Wait(1000); } catch { /* ignore */ }
        _channel?.Dispose();
        _stream?.Dispose();
        _socket?.Dispose();
        _cts?.Dispose();
    }
}

using System.Diagnostics;
using System.Net.Sockets;
using RemoteDesktop.Shared.Diagnostics;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Viewer.Net;

/// <summary>
/// Connects to a host, performs the handshake, then receives frames and measures latency — all on
/// background threads, so the picture keeps flowing (and the host's counters keep moving) even when
/// the viewer window is minimised. This is the single place all viewer socket work lives
/// (architecture rule 2 in CLAUDE.md); Stage 3 swaps the outbound target from a host IP to the relay.
/// </summary>
public sealed class ViewerClient : IDisposable
{
    private TcpClient? _client;
    private MessageChannel? _channel;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _pingLoop;

    public RateMeter IncomingMeter { get; } = new();
    public double LastLatencyMs { get; private set; }
    public bool IsConnected { get; private set; }

    public event Action<ScreenInfo>? ScreenInfoReceived;
    public event Action<FramePacket>? FrameReceived;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(string host, int port = ProtocolConstants.TcpPort)
    {
        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(host, port).ConfigureAwait(false);
        _channel = new MessageChannel(_client.GetStream());

        // Handshake: send ours, check the host's answer.
        await _channel.SendAsync(MessageType.Handshake, Handshake.Create(PeerRole.Viewer).ToBytes()).ConfigureAwait(false);
        var reply = await _channel.ReceiveAsync().ConfigureAwait(false);
        if (reply is null || reply.Value.Type != MessageType.Handshake || !Handshake.FromBytes(reply.Value.Payload).IsValid)
            throw new InvalidOperationException("The program at that address did not answer as a RemoteDesktop host.");

        IsConnected = true;
        _cts = new CancellationTokenSource();
        _receiveLoop = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        _pingLoop = Task.Run(() => PingLoopAsync(_cts.Token));
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
                await _channel!.SendAsync(MessageType.Ping, PingPayload.FromTimestamp(Stopwatch.GetTimestamp()), ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
        }
        catch { /* stops when the connection closes */ }
    }

    public void Disconnect()
    {
        _cts?.Cancel();
        try { _client?.Close(); } catch { /* ignore */ }
        IsConnected = false;
    }

    public void Dispose()
    {
        Disconnect();
        try { _receiveLoop?.Wait(1000); } catch { /* ignore */ }
        try { _pingLoop?.Wait(1000); } catch { /* ignore */ }
        _channel?.Dispose();
        _client?.Dispose();
        _cts?.Dispose();
    }
}

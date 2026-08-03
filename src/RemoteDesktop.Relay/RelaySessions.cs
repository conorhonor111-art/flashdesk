using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Relay;

/// <summary>
/// Pairs a waiting host with a calling viewer and pipes bytes between them.
///
/// The relay never looks inside a session — it copies bytes in both directions and counts them.
/// That is deliberate: it keeps the server cheap, and it means a legal demand for session content
/// cannot be satisfied because the content was never held (Stage 5 adds end-to-end encryption on
/// top, at which point it is not even theoretically available).
///
/// The parked host connection doubles as the liveness signal. There is no separate heartbeat to
/// go stale: if a host's socket drops it leaves this dictionary immediately, and a SECOND host
/// claiming a number that is already parked is refused — which is exactly the clone case from
/// CLAUDE.md, handled without any extra machinery.
/// </summary>
public sealed class RelaySessions
{
    private readonly ConcurrentDictionary<string, PendingHost> _waiting = new();
    private readonly IdRegistry _registry;

    public RelaySessions(IdRegistry registry) => _registry = registry;

    public int WaitingCount => _waiting.Count;
    public long SessionsStarted;
    public long BytesPiped;

    private sealed class PendingHost
    {
        public required WebSocket Socket { get; init; }
        public TaskCompletionSource<WebSocket> ViewerArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> SessionFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task HandleAsync(WebSocket socket, CancellationToken ct)
    {
        var hello = await ReadHelloAsync(socket, ct).ConfigureAwait(false);
        if (hello is null)
        {
            await SendJsonAsync(socket, new RelayHelloResult(false, "Bad greeting."), ct).ConfigureAwait(false);
            return;
        }

        if (hello.Role == RelayHello.RoleHost)
            await HandleHostAsync(socket, hello, ct).ConfigureAwait(false);
        else if (hello.Role == RelayHello.RoleViewer)
            await HandleViewerAsync(socket, hello, ct).ConfigureAwait(false);
        else
            await SendJsonAsync(socket, new RelayHelloResult(false, "Unknown role."), ct).ConfigureAwait(false);
    }

    private async Task HandleHostAsync(WebSocket socket, RelayHello hello, CancellationToken ct)
    {
        if (!_registry.Verify(hello.Id, hello.Secret ?? ""))
        {
            await SendJsonAsync(socket, new RelayHelloResult(false, "This number does not belong to this computer."), ct).ConfigureAwait(false);
            return;
        }

        var pending = new PendingHost { Socket = socket };

        // A number may be parked once. If an entry is already there but its socket has died
        // (crashed host, dropped link), evict it so the real machine can come back — otherwise a
        // crash would lock a client out of their own number until the relay restarted.
        while (!_waiting.TryAdd(hello.Id, pending))
        {
            if (_waiting.TryGetValue(hello.Id, out var existing) && existing.Socket.State == WebSocketState.Open)
            {
                // Genuinely online elsewhere: a cloned machine carrying the same number and secret.
                await SendJsonAsync(socket, new RelayHelloResult(false, "This number is already online on another computer."), ct).ConfigureAwait(false);
                return;
            }
            if (existing is not null) _waiting.TryRemove(hello.Id, out _);
        }

        try
        {
            await SendJsonAsync(socket, RelayHelloResult.Accepted(), ct).ConfigureAwait(false);

            // Park until a viewer calls or the relay stops. NOTHING reads this socket while it is
            // parked — a second reader would race with the pipe below, and two concurrent receives
            // on one WebSocket is an error. Liveness is handled by the eviction check above plus
            // the keep-alive pings the WebSocket layer sends for us.
            var viewer = await pending.ViewerArrived.Task.WaitAsync(ct).ConfigureAwait(false);

            Interlocked.Increment(ref SessionsStarted);
            await PipeAsync(socket, viewer, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* relay shutting down, or the host went away */ }
        finally
        {
            _waiting.TryRemove(hello.Id, out _);
            pending.ViewerArrived.TrySetCanceled();
            pending.SessionFinished.TrySetResult(true);
        }
    }

    private async Task HandleViewerAsync(WebSocket socket, RelayHello hello, CancellationToken ct)
    {
        // The caller must prove it owns the number it claims. Without this a caller could put any
        // number in the greeting, and the "you have connected with this number before" line in the
        // consent dialog — the most valuable line in it — would be trivially forgeable.
        if (!_registry.Verify(hello.Id, hello.Secret ?? ""))
        {
            await SendJsonAsync(socket, new RelayHelloResult(false, "Your own FlashDesk number could not be verified."), ct).ConfigureAwait(false);
            return;
        }

        string target = hello.TargetId ?? "";
        if (!_waiting.TryRemove(target, out var pending))
        {
            await SendJsonAsync(socket, new RelayHelloResult(false,
                "That number is not switched on right now. Ask them to open FlashDesk, then try again."), ct).ConfigureAwait(false);
            return;
        }

        await SendJsonAsync(socket, RelayHelloResult.Accepted(), ct).ConfigureAwait(false);
        await SendJsonAsync(pending.Socket, new RelayPaired(hello.Id), ct).ConfigureAwait(false);

        // Hand our socket to the host's handler, which does the piping, and stay alive until it
        // finishes — closing this method would close the viewer's socket underneath the pipe.
        pending.ViewerArrived.TrySetResult(socket);
        await pending.SessionFinished.Task.ConfigureAwait(false);
    }

    /// <summary>Copies bytes both ways until either side stops. Content is never inspected or stored.</summary>
    private async Task PipeAsync(WebSocket a, WebSocket b, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var one = CopyAsync(a, b, linked.Token);
        var two = CopyAsync(b, a, linked.Token);
        await Task.WhenAny(one, two).ConfigureAwait(false);
        linked.Cancel();
        try { await Task.WhenAll(one, two).ConfigureAwait(false); } catch { /* the other direction ending is normal */ }
    }

    private async Task CopyAsync(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && from.State == WebSocketState.Open)
            {
                var result = await from.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close) break;
                if (to.State != WebSocketState.Open) break;
                await to.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, ct)
                    .ConfigureAwait(false);
                Interlocked.Add(ref BytesPiped, result.Count);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { /* one side hung up; the pipe ends */ }
    }

    private static async Task<RelayHello?> ReadHelloAsync(WebSocket socket, CancellationToken ct)
    {
        try
        {
            var buffer = new byte[4 * 1024];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // A peer that connects and says nothing must not hold a slot (CLAUDE.md Stage 3).
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var result = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text || result.Count == 0) return null;
            return JsonSerializer.Deserialize<RelayHello>(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
        catch
        {
            return null;
        }
    }

    private static async Task SendJsonAsync<T>(WebSocket socket, T value, CancellationToken ct)
    {
        if (socket.State != WebSocketState.Open) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
        }
        catch (WebSocketException) { /* the peer went away mid-answer */ }
    }

}

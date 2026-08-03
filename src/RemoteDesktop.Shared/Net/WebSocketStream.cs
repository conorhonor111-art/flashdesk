using System.Net.WebSockets;

namespace RemoteDesktop.Shared.Net;

/// <summary>
/// Presents a WebSocket as an ordinary <see cref="Stream"/>.
///
/// Why this exists: Stage 1 and 2 spoke <see cref="Protocol.MessageChannel"/> over a raw TCP
/// stream. Stage 3 moves the same bytes over a WebSocket to the relay. Wrapping the socket in a
/// Stream means the entire protocol, framing and every message type carries over UNCHANGED — the
/// switch touches one file per side, exactly as architecture rule 2 anticipated.
///
/// A WebSocket is message-oriented and a Stream is byte-oriented, but that mismatch is harmless
/// here: <c>Read</c> is allowed to return fewer bytes than asked for, and MessageChannel already
/// loops until it has what it needs. So a receive simply returns whatever arrived.
/// </summary>
public sealed class WebSocketStream : Stream
{
    private readonly WebSocket _socket;
    private readonly bool _ownsSocket;

    public WebSocketStream(WebSocket socket, bool ownsSocket = true)
    {
        _socket = socket;
        _ownsSocket = ownsSocket;
    }

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_socket.State != WebSocketState.Open) return 0;
        try
        {
            var result = await _socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            // A close frame is the peer hanging up: report end-of-stream, which every reader
            // already treats as a clean disconnection.
            return result.MessageType == WebSocketMessageType.Close ? 0 : result.Count;
        }
        catch (WebSocketException)
        {
            return 0; // a dropped link reads as end-of-stream rather than an exception storm
        }
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        if (_socket.State != WebSocketState.Open) throw new IOException("The connection is closed.");
        await _socket.SendAsync(buffer, WebSocketMessageType.Binary, endOfMessage: true, ct).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

    protected override void Dispose(bool disposing)
    {
        if (disposing && _ownsSocket) _socket.Dispose();
        base.Dispose(disposing);
    }
}

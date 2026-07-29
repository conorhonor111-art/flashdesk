using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// Sends and receives whole messages over a byte stream (a TCP connection). Every message is
/// framed as [1 byte type][4 byte length][payload], so two messages can never run into each
/// other and a reader always knows exactly how many bytes to expect next.
/// </summary>
public sealed class MessageChannel : IDisposable
{
    private const int MaxMessageBytes = 64 * 1024 * 1024; // 64 MB guard against a corrupt length

    private readonly Stream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly byte[] _header = new byte[5];

    public MessageChannel(Stream stream) => _stream = stream;

    /// <summary>Send one message. Thread-safe against other concurrent senders.</summary>
    public async Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var header = new byte[5];
            header[0] = (byte)type;
            BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), payload.Length);
            await _stream.WriteAsync(header, ct).ConfigureAwait(false);
            if (!payload.IsEmpty)
                await _stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Receive one whole message. Returns null when the stream closes cleanly at a message boundary.
    /// </summary>
    public async Task<ReceivedMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        int read = await ReadUpToAsync(_header, ct).ConfigureAwait(false);
        if (read == 0) return null;                       // peer closed cleanly
        if (read < _header.Length) throw new EndOfStreamException("Truncated message header.");

        var type = (MessageType)_header[0];
        int length = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(1));
        if (length < 0 || length > MaxMessageBytes)
            throw new InvalidDataException($"Message length {length} out of range.");

        var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
        if (length > 0)
            await _stream.ReadExactlyAsync(payload, ct).ConfigureAwait(false);
        return new ReceivedMessage(type, payload);
    }

    // Fills the buffer; returns 0 if the stream ends before a single byte is read (clean close).
    private async Task<int> ReadUpToAsync(byte[] buffer, CancellationToken ct)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = await _stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
        }
        return total;
    }

    public void Dispose()
    {
        _sendLock.Dispose();
        _stream.Dispose();
    }
}

/// <summary>One received message: its type and raw payload bytes.</summary>
public readonly record struct ReceivedMessage(MessageType Type, byte[] Payload);

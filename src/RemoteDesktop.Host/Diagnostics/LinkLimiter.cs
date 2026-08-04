using System.Diagnostics;

namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// Deliberately slows this machine's OUTGOING data during a live session, so the adaptive ladder can
/// be watched climbing against a real screen instead of only against a simulation.
///
/// WHY IT EXISTS. The bandwidth governor has only ever been exercised by <see cref="LinkTest"/>,
/// against a simulated link and a program-drawn screen. That proves the arithmetic; it does not let
/// anyone SEE the picture soften, or check that it softens in the designed order — frame rate first,
/// quality second, and without a word of it appearing on the client's simple view. Two machines on
/// one good connection will never make the ladder move on their own, so the limit has to be
/// imposed.
///
/// ⚠️ THIS IS A TEST INSTRUMENT AND MUST NEVER BE REACHABLE BY A CLIENT. It is offered only in the
/// technical view, it starts OFF, and nothing on the wire can switch it on: the operator cannot
/// reach into a client's machine and degrade it, because no message exists that would let them. A
/// person has to choose it at the keyboard of the machine being shared.
///
/// Reads pass straight through — only sends are held back, which is the direction a home upload
/// actually limits. Holding a send back is also exactly what the governor measures, so the signal it
/// reacts to is the real one and not a special case wired in for testing.
/// </summary>
public sealed class LinkLimiter : Stream
{
    private readonly Stream _inner;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();

    private double _queuedBytes;
    private double _lastDrainMs;

    /// <summary>
    /// Kilobits per second allowed out, or 0 for no limit. Read fresh on every write, so the limit
    /// can be changed while a session is running and the ladder can be watched reacting to it.
    /// </summary>
    public volatile int Kbps;

    /// <summary>
    /// How many bytes may sit in flight before a send has to wait — a stand-in for the buffer inside
    /// a home router. This is what makes the picture fall BEHIND rather than simply thin out, and it
    /// is the case the viewer's round-trip report exists to catch.
    /// </summary>
    public volatile int BufferBytes = 512 * 1024;

    public LinkLimiter(Stream inner) => _inner = inner;

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        int kbps = Kbps;
        if (kbps > 0) await WaitForRoomAsync(buffer.Length, kbps, ct).ConfigureAwait(false);
        await _inner.WriteAsync(buffer, ct).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), ct).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    // A leaky bucket. Bytes drain at the configured rate; a send waits only when the bucket is
    // fuller than the pretend router will hold. Waiting IS the point — the governor decides from how
    // long a send took, so a limiter that dropped bytes instead of delaying them would teach it
    // nothing.
    private async Task WaitForRoomAsync(int bytes, int kbps, CancellationToken ct)
    {
        double waitMs;
        lock (_gate)
        {
            double bytesPerMs = kbps * 1000.0 / 8.0 / 1000.0;   // kbit/s -> bytes/ms
            double now = _clock.Elapsed.TotalMilliseconds;
            _queuedBytes = Math.Max(0, _queuedBytes - (now - _lastDrainMs) * bytesPerMs);
            _lastDrainMs = now;

            int capacity = Math.Max(4096, BufferBytes);
            double over = _queuedBytes + bytes - capacity;
            waitMs = over > 0 ? over / bytesPerMs : 0;
            _queuedBytes += bytes;
        }

        if (waitMs >= 1) await Task.Delay((int)Math.Min(waitMs, 5000), ct).ConfigureAwait(false);
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
        _inner.ReadAsync(buffer, ct);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        _inner.ReadAsync(buffer, offset, count, ct);

    public override bool CanRead => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek => false;
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

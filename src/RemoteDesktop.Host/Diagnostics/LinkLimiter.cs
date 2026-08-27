using System.Diagnostics;

namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// Deliberately slows this machine's traffic during a live session, so the adaptive ladder can be
/// watched climbing against a real screen instead of only against a simulation — and, since
/// 2026-08-27, so an upload can be watched contending for the channel too.
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
/// <para><b>READS ARE THROTTLED TOO, as of 2026-08-27 — this used to say the opposite, and the old
/// reasoning ("reads pass straight through — only sends are held back, which is the direction a
/// home upload actually limits") was wrong about whose upload it meant.</b> This class throttles
/// the HOST's own connection. An UPLOAD — the operator sending a file TO the client — has the
/// client's machine RECEIVING, i.e. reading here. Leaving reads unthrottled meant "Pretend the
/// connection is slow" could never simulate a slow upload at all: measured directly, a 1 GB upload
/// completed in 116.8 s (~9.4 MB/s) with 600 Kbit/s selected, no throttling effect whatsoever
/// (PROGRESS.md, 2026-08-27). That made the input-queue contention Conor cares about most
/// impossible to reproduce or measure — only a fast, unthrottled upload could ever be tested. Reads
/// and writes now drain from SEPARATE buckets (a real asymmetric home connection doesn't share one
/// pipe between its own upload and download either), same rate, same burst allowance, same
/// leaky-bucket math, applied after the fact for reads since a read's byte count isn't known until
/// it returns.</para>
/// </summary>
public sealed class LinkLimiter : Stream
{
    private readonly Stream _inner;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    /// <summary>
    /// Kilobits per second allowed, each direction, or 0 for no limit. Read fresh on every
    /// send/receive, so the limit can be changed while a session is running and the ladder — or an
    /// upload — can be watched reacting to it.
    /// </summary>
    public volatile int Kbps;

    /// <summary>
    /// How many bytes may sit in flight before a send or receive has to wait — a stand-in for the
    /// buffer inside a home router. This is what makes the picture fall BEHIND rather than simply
    /// thin out, and it is the case the viewer's round-trip report exists to catch.
    /// </summary>
    public volatile int BufferBytes = 512 * 1024;

    private readonly Bucket _writeBucket = new();
    private readonly Bucket _readBucket = new();

    public LinkLimiter(Stream inner) => _inner = inner;

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        int kbps = Kbps;
        if (kbps > 0) await _writeBucket.WaitAsync(buffer.Length, kbps, BufferBytes, _clock, ct).ConfigureAwait(false);
        await _inner.WriteAsync(buffer, ct).ConfigureAwait(false);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), ct).AsTask();

    public override void Write(byte[] buffer, int offset, int count) =>
        WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        int n = await _inner.ReadAsync(buffer, ct).ConfigureAwait(false);
        int kbps = Kbps;
        // Charged AFTER the read, not before: unlike a write, the byte count isn't known until the
        // underlying read returns, so there is nothing to size a wait against beforehand. Waiting
        // here still holds the caller back exactly as a write-side wait would, which is what lets an
        // upload's own chunk-read loop feel the same contention a download's send loop already does.
        if (kbps > 0 && n > 0) await _readBucket.WaitAsync(n, kbps, BufferBytes, _clock, ct).ConfigureAwait(false);
        return n;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
        ReadAsync(new Memory<byte>(buffer, offset, count), ct).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

    public override bool CanRead => _inner.CanRead;
    public override bool CanWrite => _inner.CanWrite;
    public override bool CanSeek => false;
    public override void Flush() => _inner.Flush();
    public override Task FlushAsync(CancellationToken ct) => _inner.FlushAsync(ct);
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    // A leaky bucket. Bytes drain at the configured rate; a send or receive waits only when the
    // bucket is fuller than the pretend router will hold. Waiting IS the point — the governor
    // decides from how long a send took, so a limiter that dropped bytes instead of delaying them
    // would teach it nothing. One instance per direction, each with its own clock reading against
    // the shared Stopwatch, so an upload filling the read bucket cannot drain the write bucket's
    // allowance or vice versa — the two directions of a real connection do not share one pipe either.
    private sealed class Bucket
    {
        private readonly object _gate = new();
        private double _queuedBytes;
        private double _lastDrainMs;

        public async Task WaitAsync(int bytes, int kbps, int bufferBytes, Stopwatch clock, CancellationToken ct)
        {
            double waitMs;
            lock (_gate)
            {
                double bytesPerMs = kbps * 1000.0 / 8.0 / 1000.0;   // kbit/s -> bytes/ms
                double now = clock.Elapsed.TotalMilliseconds;
                _queuedBytes = Math.Max(0, _queuedBytes - (now - _lastDrainMs) * bytesPerMs);
                _lastDrainMs = now;

                int capacity = Math.Max(4096, bufferBytes);
                double over = _queuedBytes + bytes - capacity;
                waitMs = over > 0 ? over / bytesPerMs : 0;
                _queuedBytes += bytes;
            }

            if (waitMs >= 1) await Task.Delay((int)Math.Min(waitMs, 5000), ct).ConfigureAwait(false);
        }
    }
}

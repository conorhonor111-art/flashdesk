using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// Latency probe. The viewer puts its own Stopwatch timestamp into a Ping; the host echoes the
/// same eight bytes back in a Pong; the viewer subtracts using its own clock. The two machines'
/// clocks are never compared, so no clock synchronisation is needed and the number cannot go
/// negative from clock skew.
/// </summary>
public static class PingPayload
{
    public static byte[] FromTimestamp(long stopwatchTimestamp)
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, stopwatchTimestamp);
        return b;
    }

    public static long ToTimestamp(ReadOnlySpan<byte> b) =>
        b.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(b) : 0;
}

using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// Latency probe. The viewer puts its own Stopwatch timestamp into a Ping; the host echoes the
/// same bytes back in a Pong; the viewer subtracts using its own clock. The two machines'
/// clocks are never compared, so no clock synchronisation is needed and the number cannot go
/// negative from clock skew.
///
/// The Ping also carries the LAST round trip the viewer measured, and that small addition is what
/// makes adaptation work on a real home connection. The sending side cannot otherwise tell the
/// difference between bytes that were delivered instantly and bytes that are sitting in a router's
/// queue: both look like a send that returned immediately. A home router can hold a megabyte, so on
/// a 0.6 MB/s uplink the picture can drift nearly two seconds behind while every local signal still
/// reads healthy — measured, not assumed (see the link test). A round trip is the only signal that
/// sees that queue, and the viewer is already measuring one every second, so reporting it back costs
/// four bytes a second and no extra traffic at all.
/// </summary>
public static class PingPayload
{
    /// <param name="lastRoundTripMs">The viewer's last measured round trip, or -1 if it has none yet.</param>
    public static byte[] FromTimestamp(long stopwatchTimestamp, int lastRoundTripMs = -1)
    {
        var b = new byte[12];
        BinaryPrimitives.WriteInt64LittleEndian(b, stopwatchTimestamp);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), lastRoundTripMs);
        return b;
    }

    public static long ToTimestamp(ReadOnlySpan<byte> b) =>
        b.Length >= 8 ? BinaryPrimitives.ReadInt64LittleEndian(b) : 0;

    /// <summary>The round trip the other side reported, or -1 when it did not report one.</summary>
    public static int ToLastRoundTripMs(ReadOnlySpan<byte> b) =>
        b.Length >= 12 ? BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8)) : -1;
}

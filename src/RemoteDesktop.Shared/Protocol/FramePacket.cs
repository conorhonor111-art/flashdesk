using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// One frame's worth of changed tiles. May contain zero tiles (nothing changed this tick); those
/// empty frames are still sent so the viewer's frame counter and the latency pings keep moving.
/// </summary>
public sealed class FramePacket
{
    public long FrameNumber { get; }
    public IReadOnlyList<TileUpdate> Tiles { get; }

    public FramePacket(long frameNumber, IReadOnlyList<TileUpdate> tiles)
    {
        FrameNumber = frameNumber;
        Tiles = tiles;
    }

    public byte[] ToBytes()
    {
        int size = 8 + 4; // frame number + tile count
        foreach (var t in Tiles) size += 12 + t.Jpeg.Length; // column + row + jpeg length + jpeg
        var b = new byte[size];
        int o = 0;
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(o), FrameNumber); o += 8;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), Tiles.Count); o += 4;
        foreach (var t in Tiles)
        {
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), t.Column); o += 4;
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), t.Row); o += 4;
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), t.Jpeg.Length); o += 4;
            t.Jpeg.CopyTo(b.AsSpan(o)); o += t.Jpeg.Length;
        }
        return b;
    }

    public static FramePacket FromBytes(ReadOnlySpan<byte> b)
    {
        int o = 0;
        long frame = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(o)); o += 8;
        int count = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
        var tiles = new List<TileUpdate>(count);
        for (int i = 0; i < count; i++)
        {
            int col = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
            int row = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
            int len = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
            var jpeg = b.Slice(o, len).ToArray(); o += len;
            tiles.Add(new TileUpdate(col, row, jpeg));
        }
        return new FramePacket(frame, tiles);
    }
}

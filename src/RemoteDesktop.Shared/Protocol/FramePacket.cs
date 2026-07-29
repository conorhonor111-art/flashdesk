using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// One frame's worth of changed tiles, plus where the host's mouse cursor is (so the viewer can draw
/// it — otherwise the remote pointer is invisible and there is no way to judge whether input lands
/// where intended). May contain zero tiles when nothing changed; those empty frames are still sent
/// so the viewer's frame counter, the cursor position and the latency pings keep moving.
/// </summary>
public sealed class FramePacket
{
    public long FrameNumber { get; }
    public int CursorX { get; }
    public int CursorY { get; }
    public bool CursorVisible { get; }
    public IReadOnlyList<TileUpdate> Tiles { get; }

    public FramePacket(long frameNumber, IReadOnlyList<TileUpdate> tiles,
        int cursorX = -1, int cursorY = -1, bool cursorVisible = false)
    {
        FrameNumber = frameNumber;
        Tiles = tiles;
        CursorX = cursorX;
        CursorY = cursorY;
        CursorVisible = cursorVisible;
    }

    public byte[] ToBytes()
    {
        int size = 8 + 4 + 4 + 1 + 4; // frame number + cursor x,y,visible + tile count
        foreach (var t in Tiles) size += 12 + t.Jpeg.Length; // column + row + jpeg length + jpeg
        var b = new byte[size];
        int o = 0;
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(o), FrameNumber); o += 8;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), CursorX); o += 4;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o), CursorY); o += 4;
        b[o] = (byte)(CursorVisible ? 1 : 0); o += 1;
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

    // Parses bytes that, at Stage 3, arrive over the internet and cannot be trusted. Every read is
    // bounds-checked against what is actually present, and nothing is allocated on an attacker-
    // supplied size, so a malformed or hostile packet fails closed (throws) instead of reading out of
    // bounds or trying to allocate gigabytes.
    private const int HeaderSize = 8 + 4 + 4 + 1 + 4; // frame + cursor x,y,visible + tile count

    public static FramePacket FromBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length < HeaderSize) throw new InvalidDataException("FramePacket too short.");

        int o = 0;
        long frame = BinaryPrimitives.ReadInt64LittleEndian(b.Slice(o)); o += 8;
        int cursorX = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
        int cursorY = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
        bool cursorVisible = b[o] != 0; o += 1;
        int count = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
        if (count < 0) throw new InvalidDataException("Negative tile count.");

        // Cap the pre-allocation; the loop still reads all `count` tiles but never trusts `count` for a
        // huge up-front allocation.
        var tiles = new List<TileUpdate>(Math.Min(count, 1024));
        for (int i = 0; i < count; i++)
        {
            if (b.Length - o < 12) throw new InvalidDataException("Truncated tile header.");
            int col = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
            int row = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
            int len = BinaryPrimitives.ReadInt32LittleEndian(b.Slice(o)); o += 4;
            if (len < 0 || len > b.Length - o) throw new InvalidDataException("Tile length out of range.");
            var jpeg = b.Slice(o, len).ToArray(); o += len;
            tiles.Add(new TileUpdate(col, row, jpeg));
        }
        return new FramePacket(frame, tiles, cursorX, cursorY, cursorVisible);
    }
}

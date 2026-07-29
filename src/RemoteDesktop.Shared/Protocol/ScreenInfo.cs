using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>Sent once when a viewer connects: the host screen size and the tile size in use.</summary>
public readonly record struct ScreenInfo(int Width, int Height, int TileSize)
{
    public int ColumnCount => (Width + TileSize - 1) / TileSize;
    public int RowCount => (Height + TileSize - 1) / TileSize;

    public byte[] ToBytes()
    {
        var b = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), Width);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), Height);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), TileSize);
        return b;
    }

    public static ScreenInfo FromBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length < 12) throw new InvalidDataException("ScreenInfo too short.");
        return new ScreenInfo(
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(0)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(4)),
            BinaryPrimitives.ReadInt32LittleEndian(b.Slice(8)));
    }
}

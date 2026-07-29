namespace RemoteDesktop.Shared.Protocol;

/// <summary>One changed tile: its column and row in the tile grid, and its JPEG-encoded pixels.</summary>
public readonly record struct TileUpdate(int Column, int Row, byte[] Jpeg);

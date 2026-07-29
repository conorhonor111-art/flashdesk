namespace RemoteDesktop.Host.Capture;

/// <summary>
/// One captured screen image, as tightly-packed 32-bit BGRA pixels (Blue, Green, Red, Alpha per
/// pixel, four bytes each). The pixel array is borrowed from the capturing object and reused every
/// frame, so it must be read before the next capture call.
/// </summary>
public readonly struct CapturedFrame
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>Packed BGRA bytes; length is Width * Height * 4.</summary>
    public byte[] Pixels { get; }

    public int Stride => Width * 4;

    public CapturedFrame(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteDesktop.Host.Encoding;

/// <summary>Encodes one tile-sized rectangle of a packed BGRA frame into JPEG bytes at a fixed quality.</summary>
public sealed class JpegTileEncoder
{
    private readonly ImageCodecInfo _jpegCodec;
    private readonly EncoderParameters _parameters;

    public JpegTileEncoder(int quality)
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        _parameters = new EncoderParameters(1)
        {
            Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality) },
        };
    }

    /// <summary>Copies a w×h tile at (srcX,srcY) out of the packed BGRA frame and returns its JPEG bytes.</summary>
    public byte[] Encode(byte[] framePixels, int frameWidth, int srcX, int srcY, int w, int h)
    {
        using var tile = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var data = tile.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int frameStride = frameWidth * 4;
            int rowBytes = w * 4;
            for (int y = 0; y < h; y++)
            {
                int srcOffset = (srcY + y) * frameStride + srcX * 4;
                IntPtr dst = data.Scan0 + y * data.Stride;
                Marshal.Copy(framePixels, srcOffset, dst, rowBytes);
            }
        }
        finally
        {
            tile.UnlockBits(data);
        }

        using var ms = new MemoryStream();
        tile.Save(ms, _jpegCodec, _parameters);
        return ms.ToArray();
    }
}

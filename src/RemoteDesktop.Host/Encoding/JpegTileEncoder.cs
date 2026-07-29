using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteDesktop.Host.Encoding;

/// <summary>
/// Encodes one tile-sized rectangle of a packed BGRA frame into JPEG bytes. The quality can be
/// changed while a session is live (the operator tunes it by eye from the host window): the encoder
/// parameters are rebuilt on change and swapped in atomically, so the frame-encoding thread always
/// reads a consistent set.
///
/// Note on chroma subsampling: GDI+ exposes only the Quality knob — there is no separate flag to
/// keep full colour detail (4:4:4). In practice GDI+ reduces chroma subsampling as quality rises, so
/// pushing quality up is also what sharpens the coloured edges of text. See the notes handed back to
/// the operator.
/// </summary>
public sealed class JpegTileEncoder
{
    private readonly ImageCodecInfo _jpegCodec;
    private volatile EncoderParameters _parameters;

    public int Quality { get; private set; }

    public JpegTileEncoder(int quality)
    {
        _jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        Quality = Math.Clamp(quality, 1, 100);
        _parameters = BuildParameters(Quality);
    }

    /// <summary>Change the quality used for subsequent tiles. Safe to call from another thread.</summary>
    public void SetQuality(int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        if (quality == Quality) return;
        Quality = quality;
        _parameters = BuildParameters(quality); // reference swap is atomic; the encode loop snapshots it
    }

    private static EncoderParameters BuildParameters(int quality) =>
        new(1) { Param = { [0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality) } };

    /// <summary>Copies a w×h tile at (srcX,srcY) out of the packed BGRA frame and returns its JPEG bytes.</summary>
    public byte[] Encode(byte[] framePixels, int frameWidth, int srcX, int srcY, int w, int h)
    {
        var parameters = _parameters; // snapshot in case the quality changes mid-frame

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
        tile.Save(ms, _jpegCodec, parameters);
        return ms.ToArray();
    }
}

using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteDesktop.Host.Capture;

/// <summary>
/// GDI BitBlt screen capture — the always-works fallback. Copies the primary screen into a bitmap
/// with Graphics.CopyFromScreen (which uses the GDI BitBlt operation underneath), then reads the
/// pixels out into a packed BGRA buffer. Slower than DXGI and cannot tell when the screen is
/// unchanged, so it always returns a frame.
/// </summary>
public sealed class GdiScreenCapture : IScreenCapture
{
    private readonly Bitmap _bitmap;
    private readonly Graphics _graphics;
    private readonly byte[] _buffer;
    private readonly Rectangle _bounds;

    public CaptureMethod Method => CaptureMethod.Gdi;
    public int Width { get; }
    public int Height { get; }

    public GdiScreenCapture(int width, int height)
    {
        Width = width;
        Height = height;
        _bounds = new Rectangle(0, 0, width, height);
        _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        _graphics = Graphics.FromImage(_bitmap);
        _buffer = new byte[width * height * 4];
    }

    public bool TryCapture(int timeoutMilliseconds, out CapturedFrame frame)
    {
        _graphics.CopyFromScreen(0, 0, 0, 0, new Size(Width, Height), CopyPixelOperation.SourceCopy);

        var data = _bitmap.LockBits(_bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = Width * 4;
            for (int y = 0; y < Height; y++)
            {
                IntPtr src = data.Scan0 + y * data.Stride;
                Marshal.Copy(src, _buffer, y * stride, stride);
            }
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }

        frame = new CapturedFrame(Width, Height, _buffer);
        return true;
    }

    public void Dispose()
    {
        _graphics.Dispose();
        _bitmap.Dispose();
    }
}

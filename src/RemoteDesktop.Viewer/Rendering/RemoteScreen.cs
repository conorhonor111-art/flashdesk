using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RemoteDesktop.Viewer.Rendering;

/// <summary>
/// The assembled remote picture. Holds ONE Bitmap for the whole session (created when ScreenInfo
/// arrives) and stamps each incoming changed tile into the right place with LockBits — never
/// allocating a new full-screen bitmap per frame, which would flood the garbage collector. Access
/// is serialised with a lock because the network thread writes tiles while the UI thread paints.
/// </summary>
public sealed class RemoteScreen : IDisposable
{
    private readonly object _gate = new();
    private Bitmap? _bitmap;

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>(Re)create the session bitmap for a new host screen size.</summary>
    public void Resize(int width, int height)
    {
        lock (_gate)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            Width = width;
            Height = height;
        }
    }

    /// <summary>Decode one tile's JPEG and copy its pixels into the session bitmap at (tileX,tileY).</summary>
    public void ApplyTile(int tileX, int tileY, byte[] jpeg)
    {
        using var ms = new MemoryStream(jpeg);
        using var tile = new Bitmap(ms);

        lock (_gate)
        {
            if (_bitmap is null) return;
            int w = Math.Min(tile.Width, Width - tileX);
            int h = Math.Min(tile.Height, Height - tileY);
            if (w <= 0 || h <= 0) return;

            var src = tile.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dst = _bitmap.LockBits(new Rectangle(tileX, tileY, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = w * 4;
                var row = new byte[rowBytes];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(src.Scan0 + y * src.Stride, row, 0, rowBytes);
                    Marshal.Copy(row, 0, dst.Scan0 + y * dst.Stride, rowBytes);
                }
            }
            finally
            {
                tile.UnlockBits(src);
                _bitmap.UnlockBits(dst);
            }
        }
    }

    /// <summary>Draw the whole picture into the destination rectangle (scaled), under the lock.</summary>
    public void DrawTo(Graphics g, Rectangle dest)
    {
        lock (_gate)
        {
            if (_bitmap is null) return;
            g.DrawImage(_bitmap, dest);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}

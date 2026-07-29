namespace RemoteDesktop.Host.Encoding;

/// <summary>
/// Splits a frame into a grid of tiles and, each frame, reports which tiles changed since the last
/// one sent — by hashing every tile's pixels and comparing to the stored hash. Only changed tiles
/// need to be JPEG-encoded and sent, which is what keeps a mostly-still support screen nearly free.
/// </summary>
public sealed class TileDiffer
{
    private readonly int _tileSize;
    private int _columns;
    private int _rows;
    private ulong[] _hashes = Array.Empty<ulong>();
    private bool _primed;

    public int Columns => _columns;
    public int Rows => _rows;

    public TileDiffer(int tileSize) => _tileSize = tileSize;

    /// <summary>Set (or reset) the grid for a given frame size. Forces the next Diff to report every
    /// tile as changed, so a newly-connected viewer receives a full first frame.</summary>
    public void Configure(int width, int height)
    {
        _columns = (width + _tileSize - 1) / _tileSize;
        _rows = (height + _tileSize - 1) / _tileSize;
        _hashes = new ulong[_columns * _rows];
        _primed = false;
    }

    /// <summary>A tile that changed: its grid position (column,row) and its pixel rectangle (x,y,w,h).</summary>
    public readonly record struct ChangedTile(int Column, int Row, int X, int Y, int Width, int Height);

    public List<ChangedTile> Diff(byte[] pixels, int frameWidth, int frameHeight)
    {
        var changed = new List<ChangedTile>();
        for (int row = 0; row < _rows; row++)
        {
            int y = row * _tileSize;
            int h = Math.Min(_tileSize, frameHeight - y);
            for (int col = 0; col < _columns; col++)
            {
                int x = col * _tileSize;
                int w = Math.Min(_tileSize, frameWidth - x);
                ulong hash = HashTile(pixels, frameWidth, x, y, w, h);
                int index = row * _columns + col;
                if (!_primed || _hashes[index] != hash)
                {
                    _hashes[index] = hash;
                    changed.Add(new ChangedTile(col, row, x, y, w, h));
                }
            }
        }
        _primed = true;
        return changed;
    }

    // FNV-1a hash over the tile's rows. Fast, and any pixel change flips it.
    private ulong HashTile(byte[] pixels, int frameWidth, int x, int y, int w, int h)
    {
        const ulong offsetBasis = 1469598103934665603UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        int frameStride = frameWidth * 4;
        int rowBytes = w * 4;
        for (int r = 0; r < h; r++)
        {
            int start = (y + r) * frameStride + x * 4;
            for (int i = 0; i < rowBytes; i++)
                hash = (hash ^ pixels[start + i]) * prime;
        }
        return hash;
    }
}

namespace RemoteDesktop.Host.Encoding;

/// <summary>
/// Splits a frame into a grid of tiles and, each frame, reports which tiles changed since the last
/// one sent — by hashing every tile's pixels and comparing to the stored hash. Only changed tiles
/// need to be JPEG-encoded and sent, which is what keeps a mostly-still support screen nearly free.
///
/// It also remembers the QUALITY each tile was last sent at, which is what makes adaptive quality
/// safe. Without that memory, adaptive quality would permanently damage the picture: tiles encoded
/// softly during a burst of motion would stay soft for the rest of the session, because a tile is
/// only re-sent when its pixels change, and a paragraph of text that has finished moving never
/// changes again. <see cref="CollectStale"/> hands those tiles back once there is spare bandwidth,
/// a few at a time, so the screen sharpens quietly instead of flashing.
/// </summary>
public sealed class TileDiffer
{
    private readonly int _tileSize;
    private int _columns;
    private int _rows;
    private ulong[] _hashes = Array.Empty<ulong>();
    private byte[] _sentQuality = Array.Empty<byte>();
    private int _refreshCursor;
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
        _sentQuality = new byte[_columns * _rows];
        _refreshCursor = 0;
        _primed = false;
    }

    /// <summary>A tile that changed: its grid position (column,row) and its pixel rectangle (x,y,w,h).</summary>
    public readonly record struct ChangedTile(int Column, int Row, int X, int Y, int Width, int Height);

    /// <param name="quality">The JPEG quality these tiles are about to be encoded at. Recorded per
    /// tile so <see cref="CollectStale"/> can find the ones left behind at a lower quality.</param>
    public List<ChangedTile> Diff(byte[] pixels, int frameWidth, int frameHeight, int quality)
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
                    _sentQuality[index] = (byte)Math.Clamp(quality, 0, 255);
                    changed.Add(new ChangedTile(col, row, x, y, w, h));
                }
            }
        }
        _primed = true;
        return changed;
    }

    /// <summary>
    /// Hands back up to <paramref name="maxTiles"/> tiles that were last sent at a quality lower than
    /// <paramref name="quality"/>, so they can be re-sent now that the link has room again. Walks the
    /// grid on a rolling cursor rather than from the top each time, so one corner of the screen cannot
    /// soak up every refresh and leave the rest soft forever.
    ///
    /// The tiles are marked as refreshed here, not after sending. If the send then fails the session
    /// is over anyway, and their pixel hashes are untouched — so any real change still re-sends them.
    /// </summary>
    public void CollectStale(int quality, int maxTiles, int frameWidth, int frameHeight, List<ChangedTile> into)
    {
        int total = _columns * _rows;
        if (total == 0 || maxTiles <= 0 || !_primed) return;

        for (int scanned = 0; scanned < total && into.Count < maxTiles; scanned++)
        {
            int index = _refreshCursor;
            _refreshCursor = (_refreshCursor + 1) % total;

            if (_sentQuality[index] >= quality) continue;

            int row = index / _columns;
            int col = index % _columns;
            int x = col * _tileSize;
            int y = row * _tileSize;
            int w = Math.Min(_tileSize, frameWidth - x);
            int h = Math.Min(_tileSize, frameHeight - y);
            if (w <= 0 || h <= 0) continue; // the grid outran a frame that shrank; skip it

            _sentQuality[index] = (byte)Math.Clamp(quality, 0, 255);
            into.Add(new ChangedTile(col, row, x, y, w, h));
        }
    }

    /// <summary>True if any tile is still sitting at a lower quality than <paramref name="quality"/>.</summary>
    public bool HasStale(int quality)
    {
        foreach (byte q in _sentQuality)
            if (q < quality) return true;
        return false;
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

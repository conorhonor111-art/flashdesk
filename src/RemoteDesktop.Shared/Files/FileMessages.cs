namespace RemoteDesktop.Shared.Files;

/// <summary>
/// How a file request ended, in the client's terms rather than the operating system's. Sent as one
/// byte; the accompanying message carries the sentence a person actually reads.
/// </summary>
public enum FileStatus : byte
{
    Ok = 0,

    /// <summary>The person at the other end pressed Refuse when asked about file access.</summary>
    RefusedByPerson = 1,

    /// <summary>The path failed the safety rules — off this machine, a device, or malformed.</summary>
    NotAllowed = 2,

    NotFound = 3,
    AccessDenied = 4,

    /// <summary>Another program has the file open in a way that blocks reading.</summary>
    InUse = 5,

    /// <summary>It existed when the listing was made and was gone by the time it was opened.</summary>
    Gone = 6,

    ReadError = 7,
    Cancelled = 8,

    /// <summary>More entries than one listing may carry. Refused rather than silently truncated.</summary>
    TooMany = 9,
}

/// <summary>
/// The file messages, kept in one file because they are one family and are only meaningful
/// together. Each follows the house style of the rest of the protocol: hand-rolled little-endian
/// binary, a ToBytes and a FromBytes, and every length that arrives from the wire checked against
/// the bytes actually present before anything is allocated. See <see cref="FileWire"/>.
///
/// REQUEST IDS ARE NEW TO THIS PROTOCOL. Nothing before this had a request and a reply to pair up —
/// the ping is correlated only by echoing its own bytes back. The viewer assigns an increasing
/// number and the host echoes it, so a reply that arrives after the operator has moved on can be
/// recognised and dropped instead of being drawn over the folder they are now looking at.
/// </summary>
public readonly record struct FileAccessRequest(int RequestId)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(4);
        FileWire.WriteInt32(b, RequestId);
        return b.ToArray();
    }

    public static FileAccessRequest FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        return new FileAccessRequest(FileWire.ReadInt32(bytes, ref o));
    }
}

/// <summary>The client's answer. <paramref name="Reason"/> is shown to the operator when refused.</summary>
public readonly record struct FileAccessReply(int RequestId, bool Granted, string Reason)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(16);
        FileWire.WriteInt32(b, RequestId);
        b.Add(Granted ? (byte)1 : (byte)0);
        FileWire.WriteString(b, Reason);
        return b.ToArray();
    }

    public static FileAccessReply FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        int id = FileWire.ReadInt32(bytes, ref o);
        bool granted = FileWire.ReadByte(bytes, ref o) != 0;
        return new FileAccessReply(id, granted, FileWire.ReadString(bytes, ref o));
    }
}

/// <summary>
/// Ask for a folder's contents. An EMPTY path means "list this machine's drives" — the root of the
/// browser, which is not itself a folder.
///
/// <para><b>LISTINGS ARE PAGED, and that is not tidiness.</b> <c>C:\Windows\WinSxS</c> holds six
/// figures of entries on an ordinary machine. Sending them in one message would build a payload of
/// several megabytes, block the message loop that also answers the latency ping, and — with longer
/// names — could reach the channel's 16 MB ceiling and drop the connection outright. So the viewer
/// asks for a window of entries and asks again for the next.</para>
/// </summary>
/// <param name="Skip">How many entries to pass over. 0 for the first page.</param>
public readonly record struct DirListRequest(int RequestId, string Path, int Skip)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(64);
        FileWire.WriteInt32(b, RequestId);
        FileWire.WriteString(b, Path);
        FileWire.WriteInt32(b, Skip);
        return b.ToArray();
    }

    public static DirListRequest FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        int id = FileWire.ReadInt32(bytes, ref o);
        string path = FileWire.ReadString(bytes, ref o);
        int skip = FileWire.ReadInt32(bytes, ref o);

        // A negative skip would mean an unbounded backward walk, and there is no legitimate sender
        // that produces one.
        if (skip < 0) throw new InvalidDataException($"Skip {skip} out of range.");

        return new DirListRequest(id, path, skip);
    }
}

/// <summary>One row in a listing. Sizes are bytes; a folder's size is always 0 and means nothing.</summary>
public sealed record DirEntry(string Name, long Size, bool IsDirectory, long ModifiedUtcTicks);

/// <summary>
/// One page of a folder's contents, or the reason there are none. <paramref name="Message"/> is the
/// sentence shown to the operator when <paramref name="Status"/> is anything but Ok.
///
/// <para><b>There is no total count, on purpose.</b> Counting the entries in
/// <c>C:\Windows\WinSxS</c> means walking every one of them before a single row can be shown, which
/// on a slow disk is seconds of a frozen panel to produce a number nobody acts on. Instead the host
/// reads one entry more than it needs and reports <paramref name="HasMore"/>. The operator is told
/// "showing the first 1,000 — there are more", which is the truth and costs nothing.</para>
/// </summary>
/// <param name="Skip">How many entries were passed over to produce this page — echoed back so a
/// reply that arrives after the operator has moved on can be recognised and dropped.</param>
/// <param name="HasMore">True when at least one more entry exists past this page.</param>
public sealed record DirListReply(
    int RequestId,
    FileStatus Status,
    string Message,
    int Skip,
    bool HasMore,
    IReadOnlyList<DirEntry> Entries)
{
    /// <summary>
    /// Entries per page. 1,000 rows of ordinary names is roughly 55 KB, and even 1,000 rows at the
    /// 255-character maximum stays near half a megabyte — comfortably clear of the channel's 16 MB
    /// ceiling, and small enough that the send lock is never held long enough to delay the latency
    /// ping behind it.
    /// </summary>
    public const int PageSize = 1000;

    public byte[] ToBytes()
    {
        var b = new List<byte>(256);
        FileWire.WriteInt32(b, RequestId);
        b.Add((byte)Status);
        FileWire.WriteString(b, Message);
        FileWire.WriteInt32(b, Skip);
        b.Add(HasMore ? (byte)1 : (byte)0);
        FileWire.WriteInt32(b, Entries.Count);
        foreach (var e in Entries)
        {
            FileWire.WriteString(b, e.Name);
            FileWire.WriteInt64(b, e.Size);
            b.Add(e.IsDirectory ? (byte)1 : (byte)0);
            FileWire.WriteInt64(b, e.ModifiedUtcTicks);
        }
        return b.ToArray();
    }

    public static DirListReply FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        int id = FileWire.ReadInt32(bytes, ref o);
        var status = (FileStatus)FileWire.ReadByte(bytes, ref o);
        string message = FileWire.ReadString(bytes, ref o);
        int skip = FileWire.ReadInt32(bytes, ref o);
        if (skip < 0) throw new InvalidDataException($"Skip {skip} out of range.");
        bool hasMore = FileWire.ReadByte(bytes, ref o) != 0;

        int count = FileWire.ReadInt32(bytes, ref o);
        if (count < 0 || count > FileWire.MaxEntries)
            throw new InvalidDataException($"Entry count {count} out of range.");

        // Capacity is deliberately NOT reserved from `count`. A sender claiming 20,000 entries
        // would otherwise get 20,000 slots allocated before a single one had been read. The list
        // grows to whatever actually arrives, and ReadString throws the moment the bytes run out.
        var entries = new List<DirEntry>();
        for (int i = 0; i < count; i++)
        {
            string name = FileWire.ReadString(bytes, ref o);
            long size = FileWire.ReadInt64(bytes, ref o);
            bool isDir = FileWire.ReadByte(bytes, ref o) != 0;
            long modified = FileWire.ReadInt64(bytes, ref o);
            entries.Add(new DirEntry(name, size, isDir, modified));
        }
        return new DirListReply(id, status, message, skip, hasMore, entries);
    }
}

/// <summary>
/// Ask for a file's bytes. <paramref name="StartOffset"/> exists so a resumed transfer is possible
/// later; today the viewer always sends 0, because resuming is only safe once we can prove the
/// remote file did not change between attempts, and nothing computes a content hash yet.
/// </summary>
public readonly record struct FileGetRequest(int RequestId, string Path, long StartOffset)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(64);
        FileWire.WriteInt32(b, RequestId);
        FileWire.WriteString(b, Path);
        FileWire.WriteInt64(b, StartOffset);
        return b.ToArray();
    }

    public static FileGetRequest FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        int id = FileWire.ReadInt32(bytes, ref o);
        string path = FileWire.ReadString(bytes, ref o);
        return new FileGetRequest(id, path, FileWire.ReadInt64(bytes, ref o));
    }
}

/// <summary>
/// A piece of a file. Bounded by <see cref="Protocol.ProtocolConstants.MaxFileChunkBytes"/>, well
/// below the channel's own 16 MB ceiling, so a transfer is limited by the chunk rather than by the
/// backstop.
/// </summary>
public sealed record FileChunk(int RequestId, long Offset, byte[] Bytes)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(Bytes.Length + 16);
        FileWire.WriteInt32(b, RequestId);
        FileWire.WriteInt64(b, Offset);
        FileWire.WriteInt32(b, Bytes.Length);
        b.AddRange(Bytes);
        return b.ToArray();
    }

    public static FileChunk FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        int id = FileWire.ReadInt32(bytes, ref o);
        long offset = FileWire.ReadInt64(bytes, ref o);
        int length = FileWire.ReadInt32(bytes, ref o);

        if (length < 0 || length > Protocol.ProtocolConstants.MaxFileChunkBytes)
            throw new InvalidDataException($"Chunk length {length} out of range.");
        if (o + length > bytes.Length)
            throw new InvalidDataException("Chunk longer than the message that carries it.");

        return new FileChunk(id, offset, bytes.Slice(o, length).ToArray());
    }
}

/// <summary>
/// The end of a transfer, however it ended. Always sent — a transfer that simply stops is
/// indistinguishable from a stalled one, and the operator would be left watching a bar that never
/// moves with nothing to read.
/// </summary>
public readonly record struct FileGetEnd(int RequestId, FileStatus Status, long TotalBytes, string Message)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(32);
        FileWire.WriteInt32(b, RequestId);
        b.Add((byte)Status);
        FileWire.WriteInt64(b, TotalBytes);
        FileWire.WriteString(b, Message);
        return b.ToArray();
    }

    public static FileGetEnd FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        int id = FileWire.ReadInt32(bytes, ref o);
        var status = (FileStatus)FileWire.ReadByte(bytes, ref o);
        long total = FileWire.ReadInt64(bytes, ref o);
        return new FileGetEnd(id, status, total, FileWire.ReadString(bytes, ref o));
    }
}

/// <summary>The operator pressed Cancel. The host stops reading and answers with a FileGetEnd.</summary>
public readonly record struct FileGetCancel(int RequestId)
{
    public byte[] ToBytes()
    {
        var b = new List<byte>(4);
        FileWire.WriteInt32(b, RequestId);
        return b.ToArray();
    }

    public static FileGetCancel FromBytes(ReadOnlySpan<byte> bytes)
    {
        int o = 0;
        return new FileGetCancel(FileWire.ReadInt32(bytes, ref o));
    }
}

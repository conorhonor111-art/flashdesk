using System.Buffers.Binary;
using System.Text;

namespace RemoteDesktop.Shared.Files;

/// <summary>
/// The reading and writing primitives every file message is built from.
///
/// They exist as one place on purpose. Each file message carries at least one length that arrives
/// from the other end of the wire, and a length read without a bound is the bug this project has
/// already had once — a hostile prefix that would have allocated 32 GB. Rather than repeat the same
/// three lines of checking in eight message types and get it right seven times, the check lives
/// here and every message goes through it.
///
/// The rule is the same everywhere: read the length, TEST IT AGAINST WHAT IS ACTUALLY PRESENT, and
/// only then allocate. Never allocate on the strength of what the sender claims.
/// </summary>
internal static class FileWire
{
    /// <summary>
    /// Longest string any file message may carry. Comfortably above a 4096-character path plus a
    /// sentence of explanation, and far below anything that matters for memory.
    /// </summary>
    internal const int MaxStringBytes = 8 * 1024;

    /// <summary>
    /// Most entries one directory listing may claim. A folder with more than this exists (Windows
    /// permits it) and is refused rather than truncated silently — a listing that quietly drops
    /// entries would have the operator believe a file is not there.
    /// </summary>
    internal const int MaxEntries = 20_000;

    internal static void WriteString(List<byte> into, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        if (utf8.Length > MaxStringBytes)
            throw new InvalidDataException($"String of {utf8.Length} bytes exceeds the {MaxStringBytes} byte limit.");

        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, utf8.Length);
        into.AddRange(len);
        into.AddRange(utf8);
    }

    /// <summary>
    /// Reads a length-prefixed UTF-8 string, advancing <paramref name="offset"/>. Throws
    /// <see cref="InvalidDataException"/> — the same type the rest of the protocol throws for a
    /// malformed frame — if the claimed length is negative, over the limit, or longer than the
    /// bytes actually present.
    /// </summary>
    internal static string ReadString(ReadOnlySpan<byte> from, ref int offset)
    {
        if (offset + 4 > from.Length)
            throw new InvalidDataException("Truncated string length.");

        int length = BinaryPrimitives.ReadInt32LittleEndian(from[offset..]);
        offset += 4;

        if (length < 0 || length > MaxStringBytes)
            throw new InvalidDataException($"String length {length} out of range.");

        // The check that matters: not "is the number sensible" but "are the bytes actually here".
        if (offset + length > from.Length)
            throw new InvalidDataException("String longer than the message that carries it.");

        string value = Encoding.UTF8.GetString(from.Slice(offset, length));
        offset += length;
        return value;
    }

    internal static void WriteInt32(List<byte> into, int value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        into.AddRange(b);
    }

    internal static void WriteInt64(List<byte> into, long value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, value);
        into.AddRange(b);
    }

    internal static int ReadInt32(ReadOnlySpan<byte> from, ref int offset)
    {
        if (offset + 4 > from.Length) throw new InvalidDataException("Truncated number.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(from[offset..]);
        offset += 4;
        return value;
    }

    internal static long ReadInt64(ReadOnlySpan<byte> from, ref int offset)
    {
        if (offset + 8 > from.Length) throw new InvalidDataException("Truncated number.");
        long value = BinaryPrimitives.ReadInt64LittleEndian(from[offset..]);
        offset += 8;
        return value;
    }

    internal static byte ReadByte(ReadOnlySpan<byte> from, ref int offset)
    {
        if (offset + 1 > from.Length) throw new InvalidDataException("Truncated message.");
        return from[offset++];
    }
}

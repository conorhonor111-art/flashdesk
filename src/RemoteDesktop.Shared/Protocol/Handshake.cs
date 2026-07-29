using System.Buffers.Binary;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>Which end of the connection a program is.</summary>
public enum PeerRole : byte
{
    Host = 1,
    Viewer = 2,
}

/// <summary>
/// The opening greeting each side sends: a magic number and version (so a wrong or mismatched
/// program is refused before any screen data flows) plus which role the sender is.
/// </summary>
public readonly record struct Handshake(uint Magic, byte Version, PeerRole Role)
{
    public static Handshake Create(PeerRole role) =>
        new(ProtocolConstants.HandshakeMagic, ProtocolConstants.ProtocolVersion, role);

    public bool IsValid =>
        Magic == ProtocolConstants.HandshakeMagic && Version == ProtocolConstants.ProtocolVersion;

    public byte[] ToBytes()
    {
        var b = new byte[6];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), Magic);
        b[4] = Version;
        b[5] = (byte)Role;
        return b;
    }

    public static Handshake FromBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length < 6) throw new InvalidDataException("Handshake too short.");
        return new Handshake(
            BinaryPrimitives.ReadUInt32LittleEndian(b),
            b[4],
            (PeerRole)b[5]);
    }
}

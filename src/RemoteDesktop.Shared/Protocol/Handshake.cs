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
/// <summary>What a peer can do beyond the original protocol. One bit each; absent means no.</summary>
[Flags]
public enum PeerCapabilities : uint
{
    None = 0,

    /// <summary>This host can list folders and send files when the person at it agrees.</summary>
    FileBrowsing = 1 << 0,

    /// <summary>
    /// This host can RECEIVE a file when the person at it agrees. A separate bit from
    /// <see cref="FileBrowsing"/> because they are separate acts and separate questions — reading
    /// somebody's disk and writing to it are not the same permission, and a build could have one
    /// without the other.
    /// </summary>
    FileUpload = 1 << 1,
}

public readonly record struct Handshake(uint Magic, byte Version, PeerRole Role, PeerCapabilities Capabilities)
{
    public static Handshake Create(PeerRole role, PeerCapabilities capabilities = PeerCapabilities.None) =>
        new(ProtocolConstants.HandshakeMagic, ProtocolConstants.ProtocolVersion, role, capabilities);

    public bool IsValid =>
        Magic == ProtocolConstants.HandshakeMagic && Version == ProtocolConstants.ProtocolVersion;

    public bool Can(PeerCapabilities capability) => (Capabilities & capability) == capability;

    /// <summary>
    /// ⚠ THE CAPABILITY FIELD IS APPENDED, AND THAT IS WHAT MAKES IT SAFE.
    ///
    /// The handshake travels as a framed message with its own length prefix, so a peer that only
    /// knows the original six bytes reads six, ignores the rest, and its stream stays perfectly
    /// aligned — the framing already told it how many bytes to consume. That is why a new build can
    /// talk to an already-downloaded one without a version bump, which would have refused every
    /// copy in the world on the day it shipped.
    ///
    /// It only works while these four bytes stay at the END. Anything inserted before them moves
    /// Role, and an old peer would read a capability byte as the role of its counterpart.
    /// </summary>
    public byte[] ToBytes()
    {
        var b = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), Magic);
        b[4] = Version;
        b[5] = (byte)Role;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(6), (uint)Capabilities);
        return b;
    }

    public static Handshake FromBytes(ReadOnlySpan<byte> b)
    {
        if (b.Length < 6) throw new InvalidDataException("Handshake too short.");

        // Six bytes is a greeting from a build that predates capabilities. It is not an error and
        // must never be treated as one: it means "this peer can do the original things only".
        var capabilities = b.Length >= 10
            ? (PeerCapabilities)BinaryPrimitives.ReadUInt32LittleEndian(b[6..])
            : PeerCapabilities.None;

        return new Handshake(
            BinaryPrimitives.ReadUInt32LittleEndian(b),
            b[4],
            (PeerRole)b[5],
            capabilities);
    }
}

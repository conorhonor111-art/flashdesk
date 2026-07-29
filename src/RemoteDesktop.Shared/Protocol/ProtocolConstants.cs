namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// Settings the host and the viewer must agree on. Defined once here so no value is ever
/// hard-coded separately on the two sides, where the two copies would drift apart.
/// </summary>
public static class ProtocolConstants
{
    /// <summary>TCP port the host listens on and the viewer connects to (Stage 1: LAN only).</summary>
    public const int TcpPort = 7789;

    /// <summary>Edge length in pixels of one square tile the screen is divided into.</summary>
    public const int TileSize = 128;

    /// <summary>JPEG quality (0-100) for changed tiles. 70 is the readable-but-small trade-off.</summary>
    public const int JpegQuality = 70;

    /// <summary>Identifies our protocol in the handshake, so a wrong program connecting is refused.</summary>
    public const uint HandshakeMagic = 0x52444B31; // ASCII "RDK1"

    /// <summary>Wire-format version. Bump when the message layout changes.</summary>
    public const byte ProtocolVersion = 1;
}

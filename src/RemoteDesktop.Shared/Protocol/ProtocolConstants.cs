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

    /// <summary>Default JPEG quality (1-100) for changed tiles. Higher = sharper text, more bytes.
    /// Text legibility is the primary quality metric for this tool. 95 is a deliberate LAN-only
    /// default (on a local network the extra bytes are free); Stage 3 MUST revisit this with
    /// adaptive quality driven by measured bandwidth, because there the bytes are the server's bill
    /// and the client's home connection. The host can change it live from its window.</summary>
    public const int DefaultJpegQuality = 95;

    /// <summary>Lowest and highest quality offered by the host's live quality control.</summary>
    public const int MinJpegQuality = 60;
    public const int MaxJpegQuality = 95;

    /// <summary>Identifies our protocol in the handshake, so a wrong program connecting is refused.</summary>
    public const uint HandshakeMagic = 0x52444B31; // ASCII "RDK1"

    /// <summary>Wire-format version. Bump when the message layout changes.</summary>
    public const byte ProtocolVersion = 1;
}

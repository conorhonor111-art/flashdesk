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

    /// <summary>Quality a session STARTS at, before it has measured anything. Higher = sharper text,
    /// more bytes; text legibility is the primary quality metric for this tool, so a session begins
    /// assuming the link is good and gives quality up only when the link says otherwise.
    /// From Stage 3 this is no longer the quality a session RUNS at: BandwidthGovernor moves it from
    /// the measured link, which is what makes the tool usable on a home connection.</summary>
    public const int DefaultJpegQuality = 95;

    /// <summary>Lowest and highest quality, for both the governor's ladder and the manual override.</summary>
    public const int MinJpegQuality = 60;
    public const int MaxJpegQuality = 95;

    /// <summary>Identifies our protocol in the handshake, so a wrong program connecting is refused.</summary>
    /// <summary>
    /// How much of a file travels in one message. Deliberately far below the channel's own 16 MB
    /// ceiling, so a transfer is bounded by the chunk rather than by the backstop.
    ///
    /// 256 KB is also small for a REASON THAT IS NOT MEMORY. Every send on a connection is
    /// serialised behind one lock, so while a chunk is being written the latency ping waits behind
    /// it — and the viewer measures that wait as round-trip time, which the bandwidth governor
    /// reads as congestion and answers by lowering the picture quality. A big chunk would make
    /// every download blur the screen. Small chunks keep that hold short.
    /// </summary>
    public const int MaxFileChunkBytes = 256 * 1024;

    public const uint HandshakeMagic = 0x52444B31; // ASCII "RDK1"

    /// <summary>Wire-format version. Bump when the message layout changes.</summary>
    public const byte ProtocolVersion = 1;

    /// <summary>
    /// Where the relay lives. One value, used by both Windows sides, so a move needs one edit.
    /// The site (flashdesk.org) is deliberately a DIFFERENT machine — see CLAUDE.md: a broken
    /// site must not be able to touch the relay.
    /// </summary>
    public const string RelayBaseUrl = "https://relay.flashdesk.org";

    /// <summary>Where sessions are paired. Same host, WebSocket scheme, port 443 like any web page.</summary>
    public const string RelayWebSocketUrl = "wss://relay.flashdesk.org/ws";
}

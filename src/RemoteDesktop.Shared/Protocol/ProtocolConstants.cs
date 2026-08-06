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

    // ---- How much of a file travels in one message. See FileChunkSize for the arithmetic; these
    // are the bounds it works between, and they are here because BOTH sides size chunks the same
    // way — the host sending a file down, the viewer pushing one up.

    /// <summary>
    /// The most a chunk may ever be, whatever the link measures. Far below the channel's own 16 MB
    /// ceiling, so a transfer is bounded by the chunk rather than by the backstop.
    /// </summary>
    public const int MaxFileChunkBytes = 256 * 1024;

    /// <summary>
    /// The least a chunk may be. Below this the per-message overhead and the lock traffic start to
    /// cost more than the bytes, and a large file would take all afternoon.
    /// </summary>
    public const int MinFileChunkBytes = 16 * 1024;

    /// <summary>
    /// What to send before the link has measured anything. Deliberately NOT the ceiling: the very
    /// first chunks of a transfer are sent in ignorance, and they are exactly the ones that would
    /// stall the picture on a slow uplink before anything had a chance to notice.
    /// </summary>
    public const int UnmeasuredFileChunkBytes = 64 * 1024;

    /// <summary>
    /// The longest one chunk should hold the connection's send lock.
    ///
    /// <para><b>THIS NUMBER IS NOT A ROUND GUESS — it is pinned under the bandwidth governor's
    /// queue threshold.</b> Every send on a connection is serialised behind one lock, so while a
    /// chunk is being written the latency ping waits behind it. The viewer measures that wait as
    /// round-trip time, and the governor treats a round trip 250 ms above the session's own best as
    /// a link backing up — and answers by dropping the picture. So a chunk that holds the lock
    /// longer than that makes every file transfer blur the screen it is not actually competing
    /// with. 200 ms leaves the margin.</para>
    ///
    /// <para>⚠ If <c>BandwidthGovernor.QueueHeavyMs</c> is ever changed, this must move with it.
    /// The comment beside that constant says the same thing from the other end.</para>
    /// </summary>
    public const int MaxChunkSendHoldMs = 200;

    /// <summary>Identifies our protocol in the handshake, so a wrong program connecting is refused.</summary>
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

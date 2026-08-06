namespace RemoteDesktop.Shared.Protocol;

/// <summary>The kind of message travelling over the wire. Sent as the one-byte type in each header.</summary>
public enum MessageType : byte
{
    Handshake = 1,
    ScreenInfo = 2,
    Frame = 3,
    Ping = 4,
    Pong = 5,
    Input = 6, // viewer -> host: one mouse or keyboard event

    /// <summary>
    /// host -> viewer: the person at the other end pressed Reject, or did not answer in time.
    /// Sent so the caller gets a truthful sentence instead of an unexplained disconnection.
    /// </summary>
    Refused = 7,

    /// <summary>
    /// host -> viewer: whether this machine can currently see its own screen, and why not if it
    /// cannot. Windows legitimately refuses screen capture on a locked desktop, a screensaver, a
    /// user-account-control password prompt, and while switching users — all ordinary moments in a
    /// support session.
    ///
    /// It exists because the alternative was worse than useless: the program used to keep sending
    /// EMPTY frames through those moments, so the connection looked perfectly healthy, the frame
    /// counter kept ticking, and the picture simply froze. Both people concluded it had crashed. A
    /// stream that looks alive and carries nothing is a lie; silence plus one plain sentence is the
    /// truth. See <see cref="ScreenStatePayload"/>.
    /// </summary>
    ScreenState = 8,

    // --- File browsing, added 2026-08-06. -------------------------------------------------------
    // An older build that receives any of these IGNORES it: neither dispatch switch has a default
    // case, so an unrecognised type is read off the wire, framed correctly, and dropped. That is
    // why these could be added without a protocol version bump, which would have cut off every copy
    // already downloaded. Whether a peer HAS them is announced in the handshake instead.

    /// <summary>viewer -> host: ask the person whether the operator may look at their files.</summary>
    FileAccessRequest = 9,

    /// <summary>host -> viewer: what they answered.</summary>
    FileAccessReply = 10,

    /// <summary>viewer -> host: list a folder. An empty path means "list the drives".</summary>
    DirListRequest = 11,

    /// <summary>host -> viewer: the folder's contents, or why there are none.</summary>
    DirListReply = 12,

    /// <summary>viewer -> host: send me this file.</summary>
    FileGetRequest = 13,

    /// <summary>host -> viewer: a piece of it, bounded by ProtocolConstants.MaxFileChunkBytes.</summary>
    FileChunk = 14,

    /// <summary>host -> viewer: the transfer is over, and how it ended. Always sent.</summary>
    FileGetEnd = 15,

    /// <summary>
    /// viewer -> host: stop transfer N. It carries nothing but a request id, so it cancels a
    /// transfer in EITHER direction — the ids are assigned by the viewer and are unique across both.
    /// The name is kept from when only downloads existed rather than renamed, because renaming a
    /// wire message that already shipped buys nothing.
    /// </summary>
    FileGetCancel = 16,

    // --- Putting a file ON the client's machine, added 2026-08-06. ------------------------------
    // Justified by the same rule that rules delete and rename OUT: with mouse control the operator
    // can already open a browser on that machine and download anything, so upload is not a new
    // power — it is an existing one made visible and logged. Where that does NOT hold, and it is
    // written down rather than glossed: a file from the operator's own disk is genuinely new, and
    // an uploaded program plus mouse control is the tech-support scam in two steps. Which is why
    // a program arriving is asked about by name, every time, and logged with its own verb.

    /// <summary>viewer -> host: I want to put this file, of this size, into this folder.</summary>
    FileSendRequest = 17,

    /// <summary>host -> viewer: whether the person agreed, and under what name it will be saved.</summary>
    FileSendReply = 18,

    /// <summary>viewer -> host: a piece of it. Same payload shape as a FileChunk, other direction.</summary>
    FileSendChunk = 19,

    /// <summary>host -> viewer: what actually happened on the disk. Always sent.</summary>
    FileSendResult = 20,
}

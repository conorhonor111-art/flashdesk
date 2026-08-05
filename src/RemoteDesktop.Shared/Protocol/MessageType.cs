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
}

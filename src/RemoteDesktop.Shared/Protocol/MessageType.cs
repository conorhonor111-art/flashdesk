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
}

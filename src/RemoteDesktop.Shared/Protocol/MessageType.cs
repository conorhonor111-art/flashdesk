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
}

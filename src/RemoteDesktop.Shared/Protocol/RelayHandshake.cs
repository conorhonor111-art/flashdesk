namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// The first thing either side says after opening the relay WebSocket, sent as one JSON text
/// message. Everything after it is the ordinary binary <see cref="MessageChannel"/> stream, which
/// the relay pipes through without understanding — the relay never sees inside a session.
///
/// Defined once here so the Windows client and the Linux relay cannot drift (architecture rule 3).
/// </summary>
public sealed record RelayHello
{
    /// <summary>"host" — I am sharing my screen and waiting to be called.</summary>
    public const string RoleHost = "host";

    /// <summary>"viewer" — I want to connect to <see cref="TargetId"/>.</summary>
    public const string RoleViewer = "viewer";

    public string Role { get; init; } = "";

    /// <summary>The caller's own FlashDesk number. Hosts must supply it; viewers may.</summary>
    public string Id { get; init; } = "";

    /// <summary>Proof that this installation owns <see cref="Id"/>. Hosts only; never logged.</summary>
    public string? Secret { get; init; }

    /// <summary>The number a viewer is calling. Viewers only.</summary>
    public string? TargetId { get; init; }
}

/// <summary>The relay's answer to <see cref="RelayHello"/>. Reason is shown to a person, so it is a sentence.</summary>
public sealed record RelayHelloResult(bool Ok, string Reason)
{
    public static RelayHelloResult Accepted() => new(true, "ok");
}

/// <summary>Sent to a waiting host the moment a viewer asks for it. After this, bytes flow.</summary>
public sealed record RelayPaired(string PeerId);

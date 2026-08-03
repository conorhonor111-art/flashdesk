namespace RemoteDesktop.Shared.Identity;

/// <summary>
/// What a client sends the relay to claim its ID. Defined once here so the Windows client and the
/// Linux relay can never drift apart (architecture rule 3).
/// </summary>
public sealed record RegistrationRequest(string Id, string Secret);

/// <summary>
/// The relay's answer. <see cref="Accepted"/> is the only thing the client's window may trust:
/// it must never display an ID the relay has not accepted, because a valid-looking number that
/// does not work is worse than an error — the client reads it aloud and both ends are confused.
/// </summary>
public sealed record RegistrationResponse(bool Accepted, RegistrationOutcome Outcome)
{
    public static RegistrationResponse Ok() => new(true, RegistrationOutcome.Registered);
    public static RegistrationResponse Refused(RegistrationOutcome outcome) => new(false, outcome);
}

public enum RegistrationOutcome
{
    /// <summary>The ID is now bound to this secret and is reachable.</summary>
    Registered = 0,

    /// <summary>Someone else already owns this ID (different secret). Generate a new one and retry.</summary>
    IdTakenByAnother = 1,

    /// <summary>The ID or secret was not a well-formed value.</summary>
    Malformed = 2,
}

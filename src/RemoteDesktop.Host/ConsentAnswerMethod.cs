namespace RemoteDesktop.Host;

/// <summary>
/// HOW a consent decision was actually made. Added 2026-09-03: a real session left Conor and Claude
/// reasoning about idle timers and an RDP session's last-active time to guess whether a click had
/// happened at all — a question the log should simply have answered. Shared by every dialog that
/// asks a yes/no safety question (<c>ConsentDialog</c>, <c>FileConsentDialog</c>, ...), so the
/// detection logic is never duplicated or allowed to drift between them.
/// </summary>
public enum ConsentAnswerMethod
{
    /// <summary>A real mouse click on the button — MouseClick fired immediately before Click.</summary>
    Clicked,

    /// <summary>Space/Enter on a focused button, or Escape via CancelButton — Click fired with no MouseClick before it.</summary>
    Keyboard,

    /// <summary>Silence for the full countdown. Never a yes.</summary>
    TimedOut,

    /// <summary>The X button or Alt+F4 — reached the FormClosing fallback without either button's Click firing.</summary>
    WindowClosed,
}

public static class ConsentAnswerMethodExtensions
{
    /// <summary>The word this prints in the session log — plain, not an enum name.</summary>
    public static string Describe(this ConsentAnswerMethod how) => how switch
    {
        ConsentAnswerMethod.Clicked => "clicked",
        ConsentAnswerMethod.Keyboard => "keyboard",
        ConsentAnswerMethod.TimedOut => "timed out",
        ConsentAnswerMethod.WindowClosed => "window closed",
        _ => "unknown",
    };
}

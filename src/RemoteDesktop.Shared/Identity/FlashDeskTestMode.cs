namespace RemoteDesktop.Shared.Identity;

/// <summary>
/// One switch, read from the environment, that makes remote control physically impossible for this
/// process — as viewer (the checkbox is disabled so it can never be checked) AND as host (an
/// incoming input message is never applied, even if one somehow arrives).
///
/// <para><b>Why it has to cover both directions.</b> This exists for running two copies of FlashDesk
/// on ONE physical machine (see <see cref="FlashDeskFolder"/> — same reason FLASHDESK_CONFIG_DIR
/// exists) so relay pairing can be tested without a second computer. On a real two-machine session,
/// injected input lands on the OTHER person's desk — that is the product. On one machine, the same
/// SendInput call would move the real cursor on the machine doing the testing: with two people typing
/// numbers into each other's windows there is no way to know in advance which of the two processes
/// will end up on the receiving end of a control request, so the copy carrying this flag refuses to
/// send a control request AND refuses to act on one, whichever role it is given.</para>
///
/// <para>Consent is untouched by this: the Accept dialog still requires a human click either way.
/// This only removes the ability to move the mouse once a session is live.</para>
/// </summary>
public static class FlashDeskTestMode
{
    /// <summary>True if this process must never actually inject input, in either direction.</summary>
    public static bool ControlDisabled =>
        Environment.GetEnvironmentVariable("FLASHDESK_DISABLE_CONTROL") is "1" or "true";

    /// <summary>
    /// True if this process should skip straight to GDI capture rather than attempting DXGI first.
    ///
    /// <para>Windows only lets ONE process hold a DXGI Desktop Duplication session on a given output
    /// at a time. Two copies of FlashDesk on the same physical machine both capturing the primary
    /// monitor is a new situation this project only started hitting once running two copies on one
    /// machine became a supported way to test — the second copy to start cannot get DXGI, spends 30
    /// seconds failing to recreate the duplication (see DxgiScreenCapture.GiveUpAfterMs), then falls
    /// back to GDI anyway. Confirmed against the real capture log, 2026-08-26 07:09: "DXGI access
    /// lost" followed 30538 ms later by "fell back to Gdi". Skipping straight to GDI for the second
    /// copy gets the same end state without the 30 s of red "Capture failures" alarm along the way —
    /// this is not fixing a bug, DXGI genuinely cannot be shared; it is not making the second copy
    /// wait to learn something already known.</para>
    /// </summary>
    public static bool ForceGdi =>
        Environment.GetEnvironmentVariable("FLASHDESK_FORCE_GDI") is "1" or "true";
}

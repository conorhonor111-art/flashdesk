namespace RemoteDesktop.Shared.Identity;

/// <summary>
/// The one folder FlashDesk keeps everything it remembers in, on this machine, for this Windows
/// account: the identity, the known callers, the session log, and the ledger of unfinished
/// transfers.
///
/// <para>It lives here rather than inside <c>IdentityStore</c> because more than one side now needs
/// it — the host for its identity and log, the operator's side for its own unfinished-download
/// ledger — and two copies of this rule would drift. A drifted copy would not crash; it would
/// quietly keep a second, separate set of files, and the symptom would be a client's 9-digit number
/// changing for no reason.</para>
///
/// <para><b>It is under the user's own profile, so writing it needs no administrator rights</b>, and
/// it is never beside the executable. That is what makes the number survive deleting and
/// re-downloading the program.</para>
/// </summary>
public static class FlashDeskFolder
{
    /// <summary>
    /// Where to keep things. <c>FLASHDESK_CONFIG_DIR</c> overrides it so two copies can run side by
    /// side on one machine with separate numbers — which is how relay pairing is tested without a
    /// second computer. Note it cannot be done by setting <c>APPDATA</c>: Windows resolves the
    /// Application Data folder through the shell, not that environment variable.
    /// </summary>
    public static string Current
    {
        get
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FLASHDESK_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(overrideDir)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlashDesk")
                : overrideDir;
        }
    }
}

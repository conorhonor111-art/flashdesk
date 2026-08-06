namespace RemoteDesktop.Shared.Files;

/// <summary>
/// Decides whether a path sent by the operator may be opened on the client's machine.
///
/// THIS IS THE FIRST CODE IN FLASHDESK TO TURN A STRING FROM THE WIRE INTO A PATH ON DISK.
/// Everywhere else the program combines a literal filename with a folder it chose itself. So there
/// is no prior art here, and the failure it guards against is not a crash — it is a stranger's
/// private file leaving their computer because a check was written to look right rather than to be
/// right. Its tests were written before it existed, in RemotePathTests.
///
/// WHAT THIS DECIDES, AND WHAT IT DOES NOT. Everything here is decidable from the string alone:
/// shape, canonical form, and whether the path reaches off this machine. It deliberately does NOT
/// check that the drive is a local disk, because that needs <c>DriveInfo</c>, which only means
/// anything on the machine actually running the host — that check lives beside the drive
/// enumeration in the Host project, and BOTH must pass. A caller that uses this alone is not
/// finished.
///
/// It lives in Shared only because that is what the test project can reference. The rules it
/// encodes are Windows rules; nothing on the Linux relay calls it.
/// </summary>
public static class RemotePath
{
    /// <summary>
    /// Longest path accepted. Windows tolerates 32767 with the extended prefix, but nothing
    /// legitimate in a support session is near this, and an unbounded string is an invitation.
    /// </summary>
    public const int MaxLength = 4096;

    /// <summary>
    /// Names Windows still resolves as devices in some contexts, whatever folder they appear in.
    /// Matched without the extension, so "CON.txt" is caught as well as "CON".
    /// </summary>
    private static readonly string[] ReservedNames =
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Returns true and the canonical path when the request is acceptable; false and a plain
    /// sentence when it is not. The sentence is shown to the operator, so it is never empty — a
    /// blank message surfaces as an empty box and reads as a crash.
    /// </summary>
    public static bool TryResolve(string? requested, out string? full, out string? problem)
    {
        full = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(requested))
        {
            problem = "No location was given.";
            return false;
        }

        // An embedded NUL truncates the string inside any Win32 call, so what Windows opens would
        // not be what was checked here. Rejected before anything else looks at it.
        if (requested.IndexOf('\0') >= 0)
        {
            problem = "That location contains a character that is not allowed.";
            return false;
        }

        if (requested.Length > MaxLength)
        {
            problem = "That location is too long.";
            return false;
        }

        // Fully qualified means the drive is named IN the string. Anything else — "..\Windows",
        // "Users\Public", even "\Windows" — takes its drive or its starting point from somewhere
        // else, and that somewhere else is not anything the client agreed to expose.
        if (!Path.IsPathFullyQualified(requested))
        {
            problem = "That location must include a drive, like C:\\Users.";
            return false;
        }

        string canonical;
        try
        {
            // Resolves "." and ".." and normalises separators. EVERY check below runs on the
            // result, never on the original: approving the raw string and then passing the raw
            // string on is the classic way traversal survives a review.
            canonical = Path.GetFullPath(requested);
        }
        catch (Exception)
        {
            problem = "That location could not be understood.";
            return false;
        }

        // One test for three different reaches, all of which begin with two separators once
        // canonicalised: \\server\share (another machine), \\.\Device (not a file at all),
        // \\?\C:\... and \\?\UNC\... (the extended namespaces, which also bypass the normalisation
        // that everything above depends on).
        if (canonical.StartsWith(@"\\", StringComparison.Ordinal))
        {
            problem = "Only this computer's own drives can be opened.";
            return false;
        }

        // After all that, the path must still be an ordinary drive-letter path.
        if (canonical.Length < 3 || canonical[1] != ':' || !char.IsLetter(canonical[0]))
        {
            problem = "That location is not on a drive of this computer.";
            return false;
        }

        if (HasReservedName(canonical))
        {
            problem = "That name is reserved by Windows and is not a file.";
            return false;
        }

        full = canonical;
        return true;
    }

    private static bool HasReservedName(string canonical)
    {
        foreach (string segment in canonical.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (segment.Length == 0) continue;

            // "CON.txt" is still CON. Compare the part before the first dot.
            int dot = segment.IndexOf('.');
            string stem = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');

            foreach (string reserved in ReservedNames)
            {
                if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }
}

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

        // ⚠ THE SECOND COLON IS AN ALTERNATE DATA STREAM. `report.txt:hidden` is a separate body of
        // bytes attached to a file, and it does not appear in any listing — not ours, not Explorer's.
        // Without this, an operator could read content the person at that machine has no way of even
        // knowing is there, from a file whose visible contents are innocent. The write side has
        // refused this since it was written (IsSafeFileName rejects the colon outright); the read
        // side did not, and reading is the side where the secrecy matters.
        // Found by an adversarial read of this file, 2026-08-06.
        if (canonical.IndexOf(':', 2) >= 0)
        {
            problem = "That name refers to hidden data inside a file, which cannot be opened.";
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

    // ============================================================================================
    // THE WRITE SIDE. Reading and writing are not the same risk and are not checked the same way.
    // Reading is allowed across the whole drive, so a path only has to be a real place on this
    // machine. Writing is confined to ONE folder the operator chose, because writing is where
    // damage is permanent — and that confinement rests on two things: the filename must be a BARE
    // NAME, and the containment test must run on the RESOLVED path, never on the joined string.
    // ============================================================================================

    /// <summary>Longest filename Windows accepts in a single path segment.</summary>
    public const int MaxFileNameLength = 255;

    /// <summary>
    /// True when <paramref name="name"/> is a plain filename that cannot move a write anywhere but
    /// into the folder it is joined to.
    /// </summary>
    public static bool IsSafeFileName(string? name, out string? problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(name))
        {
            problem = "No file name was given.";
            return false;
        }

        if (name.Length > MaxFileNameLength)
        {
            problem = "That file name is too long.";
            return false;
        }

        if (name.IndexOf('\0') >= 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            // GetInvalidFileNameChars covers both separators and the colon, so this one test
            // catches "sub\file.txt", "sub/file.txt", "C:evil.txt" and "report.txt:hidden" — the
            // alternate-data-stream form that writes bytes Explorer does not show.
            problem = "That file name contains characters that are not allowed.";
            return false;
        }

        if (name == "." || name == "..")
        {
            problem = "That is not a file name.";
            return false;
        }

        // ⚠ Windows SILENTLY STRIPS a trailing dot or space. "evil.exe." becomes "evil.exe" AFTER
        // any check that saw a different name — so the thing written is not the thing approved.
        // Refused rather than trimmed: a name that changes between being checked and being used is
        // the shape of a bypass, and trimming it would hide that rather than stop it.
        if (name.EndsWith('.') || name.EndsWith(' '))
        {
            problem = "A file name cannot end with a dot or a space.";
            return false;
        }

        if (IsReservedStem(name))
        {
            problem = "That name is reserved by Windows and is not a file.";
            return false;
        }

        // ⚠ A NAME THAT DOES NOT READ THE WAY IT IS SPELLED. Unicode has characters that reverse the
        // direction of the text after them, and Windows, our own dialogs and Explorer all honour
        // them. `Invoice‮xcod.exe` is displayed as `Invoiceexe.docx` — so the ONE line on the
        // consent dialog whose job is to say what is arriving would show a document while a program
        // arrived. The whole feature rests on that line being true, so a name containing one of
        // these is refused rather than displayed. Found by an adversarial read, 2026-08-06.
        foreach (char c in name)
        {
            if (IsDirectionOverride(c))
            {
                problem = "That file name contains characters that are not allowed.";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The Unicode characters that change which way following text is displayed: the explicit
    /// bidirectional marks and the isolates. Listed by code point rather than by category, because
    /// "is this character invisible" is a much larger and more arguable question than "can this
    /// character make a name read backwards".
    /// </summary>
    /// <remarks>Written as escapes, not as the characters themselves: these are INVISIBLE, so a
    /// literal here would be a rule nobody could see or review.</remarks>
    private static bool IsDirectionOverride(char c) => c is
        (char)0x200E or (char)0x200F              // left-to-right / right-to-left mark
        or >= (char)0x202A and <= (char)0x202E    // the embeddings and the two overrides
        or >= (char)0x2066 and <= (char)0x2069;   // the isolates

    /// <summary>
    /// Joins a bare filename to the folder the operator chose and proves the result is still inside
    /// it. Returns false, with a reason, for anything else.
    ///
    /// <para>The containment test compares against the folder PLUS a separator. Without that,
    /// <c>C:\Temp2</c> passes a check for <c>C:\Temp</c> — the prefix bug — and a file lands in a
    /// folder nobody chose.</para>
    ///
    /// <para><b>This is not the whole defence.</b> A junction placed at the destination between
    /// this check and the write redirects it, and no string test can see that. The handle must be
    /// re-checked after opening, and that belongs with the code that does the opening.</para>
    /// </summary>
    public static bool TryResolveInside(string? folder, string? name, out string? full, out string? problem)
    {
        full = null;

        if (!TryResolve(folder, out string? canonicalFolder, out problem)) return false;
        if (!IsSafeFileName(name, out problem)) return false;

        string root = canonicalFolder!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(root, name!));
        }
        catch (Exception)
        {
            problem = "That destination could not be understood.";
            return false;
        }

        // The separator is what makes this containment rather than string matching.
        string prefix = root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            problem = "That file would be written outside the chosen folder.";
            return false;
        }

        full = candidate;
        return true;
    }

    /// <summary>
    /// File types Windows will run, or that run something, so the person being asked can be told a
    /// PROGRAM is arriving rather than a file. Not a security boundary — the list cannot be
    /// complete and a renamed executable defeats it — it exists so the consent question can say
    /// what is actually happening.
    /// </summary>
    public static bool LooksExecutable(string name)
    {
        string ext = Path.GetExtension(name);
        foreach (string e in ExecutableExtensions)
            if (ext.Equals(e, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static readonly string[] ExecutableExtensions =
    {
        ".exe", ".com", ".scr", ".pif",
        ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".msi", ".msp", ".msc",
        ".dll", ".cpl", ".ocx",
        ".lnk", ".url", ".reg", ".hta", ".jar",
    };

    private static bool IsReservedStem(string segment)
    {
        int dot = segment.IndexOf('.');
        string stem = (dot >= 0 ? segment[..dot] : segment).TrimEnd(' ');
        foreach (string reserved in ReservedNames)
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
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

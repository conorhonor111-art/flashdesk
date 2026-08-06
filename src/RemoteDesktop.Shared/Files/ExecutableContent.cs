namespace RemoteDesktop.Shared.Files;

/// <summary>
/// Looks at what a file ACTUALLY IS, from its first bytes, rather than at what it is called.
///
/// <para><b>WHY THIS EXISTS.</b> <see cref="RemotePath.LooksExecutable"/> trusts the extension, and
/// an extension is chosen by whoever sends the file. Rename <c>virus.exe</c> to <c>invoice.pdf</c>
/// and the person at the other end is asked "a file is coming" instead of "a program is coming" —
/// then the operator renames it back with the mouse they already have. The consent question would
/// have been answered truthfully and still been wrong.</para>
///
/// <para><b>⚠ HONEST LIMIT, AND IT IS A REAL ONE.</b> This catches compiled programs, which have a
/// signature in their first bytes. It CANNOT catch a script — a <c>.bat</c>, <c>.cmd</c> or
/// <c>.ps1</c> is plain text with nothing to recognise, and renaming one to <c>invoice.pdf</c>
/// defeats both this check and the extension check. Nothing short of refusing all unknown content
/// would close that, and the honest answer is that the visible record is what covers it: whatever
/// arrives is named, sized and logged on the client's own machine.</para>
///
/// <para>Deliberately NOT flagged, because the false alarms would be worse than the miss:
/// ZIP-family containers (<c>PK</c>), which are also every .docx, .xlsx and .odt; and OLE compound
/// files (<c>D0CF11E0</c>), which are .msi but equally every legacy .doc and .xls. Telling someone
/// "a program is coming" about their own Word document would teach them the warning is noise, and a
/// warning people learn to ignore protects nobody.</para>
/// </summary>
public static class ExecutableContent
{
    /// <summary>
    /// How many leading bytes are needed to decide. The host must have at least this many before
    /// asking the question — which is why the check happens on the FIRST CHUNK to arrive, not on
    /// the finished file.
    /// </summary>
    public const int BytesNeeded = 8;

    /// <summary>
    /// True when these opening bytes belong to something Windows will run.
    /// </summary>
    public static bool Looks(ReadOnlySpan<byte> firstBytes)
    {
        if (firstBytes.Length < 2) return false;

        // "MZ" — every Windows executable image: .exe, .dll, .scr, .cpl, .ocx, .sys, and a .com
        // built by any modern compiler. This is the one that matters.
        if (firstBytes[0] == 0x4D && firstBytes[1] == 0x5A) return true;

        if (firstBytes.Length < 8) return false;

        // A Windows shortcut. It is not a program itself, but it POINTS at one, and double-clicking
        // it runs whatever it points at — including something already on the machine. Same question
        // for the person being asked, so the same answer here.
        if (firstBytes[0] == 0x4C && firstBytes[1] == 0x00 && firstBytes[2] == 0x00 && firstBytes[3] == 0x00 &&
            firstBytes[4] == 0x01 && firstBytes[5] == 0x14 && firstBytes[6] == 0x02 && firstBytes[7] == 0x00)
            return true;

        return false;
    }

    /// <summary>
    /// What the client is asked, given what the name claims and what the bytes say.
    /// </summary>
    public static ContentVerdict Judge(string fileName, ReadOnlySpan<byte> firstBytes)
    {
        bool nameSaysProgram = RemotePath.LooksExecutable(fileName);
        bool bytesSayProgram = Looks(firstBytes);

        // The case this whole class exists for: the content is a program and the name hides it.
        // The transfer stops and the question is asked again, honestly.
        if (bytesSayProgram && !nameSaysProgram) return ContentVerdict.ProgramInDisguise;

        if (bytesSayProgram || nameSaysProgram) return ContentVerdict.Program;

        return ContentVerdict.OrdinaryFile;
    }
}

/// <summary>What is arriving, in the terms the person at the receiving machine needs.</summary>
public enum ContentVerdict
{
    OrdinaryFile,

    /// <summary>A program, and its name says so. Ask about a program; log it as one.</summary>
    Program,

    /// <summary>
    /// A program wearing another kind of name. The transfer STOPS and the person is asked again
    /// with wording that says what it really is. Logged as a program either way — the log records
    /// what arrived, not what it was called.
    /// </summary>
    ProgramInDisguise,
}

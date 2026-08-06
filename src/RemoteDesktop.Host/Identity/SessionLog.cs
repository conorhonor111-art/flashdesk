namespace RemoteDesktop.Host.Identity;

/// <summary>
/// A plain text record, on the client's own machine, of every connection this computer was asked
/// for: who asked, when, and what was decided. Hard rule 4 in CLAUDE.md §4 — the person must be
/// able to open a file and see who has been on their machine.
///
/// It records WHO and WHEN, and — since file transfer exists — WHAT WAS MOVED. No screen content,
/// no keystrokes, nothing that was typed or looked at. It never leaves this machine.
///
/// ⚠ THIS PROMISE CHANGED ON 2026-08-06 AND THE CHANGE IS DELIBERATE. Until then this comment said
/// the log recorded "no file names" and "deliberately cannot answer anything more". That was the
/// right promise while the operator could only look at a screen: a screen shows what the person is
/// already looking at, so there was nothing to account for afterwards. Copying a file OFF someone's
/// machine, or putting one ON it, is a different act — it leaves something behind, or takes
/// something away, and the person has no other way to find out what. A log that recorded the
/// connection but not the file would be protecting the operator's privacy instead of the client's.
/// So file names are now recorded, ON PURPOSE, and the old promise is retired rather than quietly
/// outgrown. What is still never recorded: anything on the screen, anything typed, and the CONTENTS
/// of any file.
/// </summary>
public sealed class SessionLog
{
    private readonly string _file;
    private readonly object _gate = new();

    public SessionLog(string folder)
    {
        _file = Path.Combine(folder, "sessions.txt");
    }

    public string FilePath => _file;

    public void Started(string callerId) => Write($"CONNECTED    {Pretty(callerId)} accepted and connected");
    public void Ended(string callerId) => Write($"DISCONNECTED {Pretty(callerId)} session ended");
    public void Refused(string callerId) => Write($"REFUSED      {Pretty(callerId)} was refused (Reject, or no answer)");

    // --- File transfer. Every line names the direction, the file, its size and the folder, because
    // "a file was transferred" answers nothing a person actually wants to know. The verb column is
    // padded to 13 characters like the three above, so the file stays readable as one table.

    /// <summary>The operator was asked whether they may look at this computer's files.</summary>
    public void FilesAsked(string callerId) =>
        Write($"FILES        {Pretty(callerId)} asked to look at the files on this computer");

    public void FilesAllowed(string callerId) =>
        Write($"FILES        {Pretty(callerId)} was allowed to look at the files on this computer");

    public void FilesRefused(string callerId) =>
        Write($"FILES        {Pretty(callerId)} was refused when asking to look at the files");

    /// <summary>A file left this computer.</summary>
    public void FileSent(string callerId, string name, long bytes, string folder) =>
        Write($"SENT         {Pretty(callerId)} copied \"{name}\" ({Size(bytes)}) from {folder}");

    /// <summary>A file arrived on this computer.</summary>
    public void FileReceived(string callerId, string name, long bytes, string folder) =>
        Write($"RECEIVED     {Pretty(callerId)} put \"{name}\" ({Size(bytes)}) into {folder}");

    /// <summary>
    /// A PROGRAM arrived. Logged with its own verb at Conor's instruction, because a person
    /// scanning this file afterwards needs to see that software was placed on their machine —
    /// which is the step a tech-support scam depends on — without having to recognise what ".exe"
    /// means among a list of ordinary file names.
    /// </summary>
    public void ProgramReceived(string callerId, string name, long bytes, string folder) =>
        Write($"PROGRAM      {Pretty(callerId)} put the program \"{name}\" ({Size(bytes)}) into {folder}");

    /// <summary>An existing file was replaced, after the person at this machine agreed to it.</summary>
    public void FileReplaced(string callerId, string name, string folder) =>
        Write($"REPLACED     {Pretty(callerId)} replaced \"{name}\" in {folder}");

    /// <summary>
    /// Appends the block SessionRecorder produced, unstamped and indented under the DISCONNECTED
    /// line it belongs to. This is how a tester reports a session: they send this file instead of
    /// reading numbers off a screen while someone talks to them on the phone.
    ///
    /// It describes the CONNECTION — how fast, how steady, how much data — never anything that was
    /// on the screen.
    /// </summary>
    public void Detail(string block) => Append(block + Environment.NewLine);

    /// <summary>
    /// A size a person reads, not a number of bytes. "2.4 MB" tells someone whether that was their
    /// holiday photos or a spreadsheet; "2514763" tells them nothing without arithmetic.
    /// </summary>
    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.##} GB",
    };

    private static string Pretty(string id) =>
        id.Length == 9 ? $"{id[..3]} {id[3..6]} {id[6..]}" : id;

    private void Write(string line) =>
        Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {line}{Environment.NewLine}");

    private void Append(string text)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                // Plain ASCII and a UTF-8 BOM: this file is opened in Notepad by a non-technical
                // person, and an em-dash written without a BOM came out as mojibake (seen in test).
                var encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                if (!File.Exists(_file))
                    File.WriteAllText(_file,
                        "FlashDesk - record of who connected to this computer." + Environment.NewLine +
                        "Times are this computer's local time." + Environment.NewLine + Environment.NewLine,
                        encoding);
                File.AppendAllText(_file, text, encoding);
            }
            catch { /* a log that cannot be written must never take the session down */ }
        }
    }
}

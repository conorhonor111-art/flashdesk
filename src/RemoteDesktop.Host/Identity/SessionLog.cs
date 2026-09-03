using RemoteDesktop.Host;

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

    /// <summary>
    /// Set the moment a write to this file fails, cleared the moment one succeeds. Null means either
    /// "never tried" or "the last attempt worked" — read <see cref="LastWriteErrorAtUtc"/> together
    /// with it if that distinction matters.
    ///
    /// <para>Added 2026-08-27 after a real three-hour session vanished with nothing on disk and
    /// nothing to say why: <see cref="Append"/>'s catch was silent on purpose ("a log that cannot be
    /// written must never take the session down"), which was right about not crashing the session
    /// and wrong about leaving no trace — the two are not the same requirement. A failure that looks
    /// identical to nothing having happened cannot be told apart from nothing having happened.</para>
    /// </summary>
    public string? LastWriteError { get; private set; }
    public DateTime? LastWriteErrorAtUtc { get; private set; }

    /// <summary>Fired every time a write fails, with the exception message. See <see cref="LastWriteError"/>.</summary>
    public event Action<string>? WriteFailed;

    /// <summary>
    /// Someone is asking to connect, BEFORE the dialog is shown — the other half of the pair
    /// completed 2026-09-03. FILES already had "asked" + "was allowed/refused"; the connection
    /// accept had only the outcome, with no line marking that a question was ever put. Added so a
    /// reader can see a decision was actually asked for, not merely reason backwards from its result.
    /// </summary>
    public void ConnectAsked(string callerId) =>
        Write($"CONNECT      {Pretty(callerId)} is asking to connect");

    /// <summary>
    /// <paramref name="how"/> is <see cref="ConsentAnswerMethod.Describe"/> — clicked, keyboard, or
    /// timed out. Added 2026-09-03: without it, "was this really answered, or did something answer
    /// it for me" could only be reasoned about from idle timers, never read off the file it belongs
    /// in. See ConsentAnswerMethod's own doc comment for the incident that produced this.
    /// </summary>
    public void Started(string callerId, string how) =>
        Write($"CONNECTED    {Pretty(callerId)} accepted and connected ({how})");
    public void Ended(string callerId) => Write($"DISCONNECTED {Pretty(callerId)} session ended");
    public void Refused(string callerId, string how) =>
        Write($"REFUSED      {Pretty(callerId)} was refused ({how})");

    // --- File transfer. Every line names the direction, the file, its size and the folder, because
    // "a file was transferred" answers nothing a person actually wants to know. The verb column is
    // padded to 13 characters like the three above, so the file stays readable as one table.

    /// <summary>The operator was asked whether they may look at this computer's files.</summary>
    public void FilesAsked(string callerId) =>
        Write($"FILES        {Pretty(callerId)} asked to look at the files on this computer");

    public void FilesAllowed(string callerId, string how) =>
        Write($"FILES        {Pretty(callerId)} was allowed to look at the files on this computer ({how})");

    public void FilesRefused(string callerId, string how) =>
        Write($"FILES        {Pretty(callerId)} was refused when asking to look at the files ({how})");

    /// <summary>A file left this computer.</summary>
    public void FileSent(string callerId, string name, long bytes, string folder) =>
        Write($"SENT         {Pretty(callerId)} copied \"{name}\" ({Size(bytes)}) from {folder}");

    // --- Incoming files and programs. Added 2026-09-03: this pair used to have NO "asked" line and
    // NO record at all of a refusal — only a successful arrival was ever logged, so a person scanning
    // the file afterwards could not tell "nothing was ever offered" from "something was offered and
    // refused". This is exactly the gap Conor named: "the PROGRAM line logged the result with no
    // record of the asking." Matches the FILES asked/allowed/refused shape already in place above.

    /// <summary>They are asking to put a file, or a program, on this computer — before the dialog shows.</summary>
    public void IncomingAsked(string callerId, string name, long bytes, string folder, bool isProgram) =>
        Write($"UPLOAD       {Pretty(callerId)} is asking to put {(isProgram ? "the program " : "")}"
            + $"\"{name}\" ({Size(bytes)}) into {folder}");

    public void IncomingRefused(string callerId, string name, bool isProgram, string how) =>
        Write($"UPLOAD       {Pretty(callerId)} was refused when asking to put "
            + $"{(isProgram ? "the program " : "")}\"{name}\" here ({how})");

    /// <summary>A file arrived on this computer.</summary>
    public void FileReceived(string callerId, string name, long bytes, string folder, string how) =>
        Write($"RECEIVED     {Pretty(callerId)} put \"{name}\" ({Size(bytes)}) into {folder} ({how})");

    /// <summary>
    /// A PROGRAM arrived. Logged with its own verb at Conor's instruction, because a person
    /// scanning this file afterwards needs to see that software was placed on their machine —
    /// which is the step a tech-support scam depends on — without having to recognise what ".exe"
    /// means among a list of ordinary file names.
    /// </summary>
    public void ProgramReceived(string callerId, string name, long bytes, string folder, string how) =>
        Write($"PROGRAM      {Pretty(callerId)} put the program \"{name}\" ({Size(bytes)}) into {folder} ({how})");

    /// <summary>
    /// Unfinished files from an interrupted transfer were removed at start-up. It has no caller
    /// number because nobody was connected when it happened — this is the program tidying up after
    /// a link that died, and the person is told rather than having it done silently on their disk.
    /// Only written when there was something to remove; a line on every start would be noise in the
    /// one file this person is meant to be able to read.
    /// </summary>
    public void UnfinishedRemoved(int count) =>
        Write($"CLEANED      removed {count} unfinished {(count == 1 ? "file" : "files")} "
            + "left behind by a transfer that was cut off");

    /// <summary>Same reasoning as IncomingAsked/IncomingRefused above — the destructive one of the two
    /// dialogs (Replace throws away a file that cannot be got back) had no asked line and no record
    /// of a refusal either.</summary>
    public void ReplaceAsked(string callerId, string name, string folder) =>
        Write($"OVERWRITE    {Pretty(callerId)} is asking whether to replace \"{name}\" in {folder}");

    public void ReplaceRefused(string callerId, string name, string how) =>
        Write($"OVERWRITE    {Pretty(callerId)} was refused when asking to replace \"{name}\" ({how})");

    /// <summary>An existing file was replaced, after the person at this machine agreed to it.</summary>
    public void FileReplaced(string callerId, string name, string folder, string how) =>
        Write($"REPLACED     {Pretty(callerId)} replaced \"{name}\" in {folder} ({how})");

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
    /// ONE short line, written periodically WHILE the session is still running. Added 2026-08-27,
    /// same reasoning as <see cref="TransferCheckpoint"/> below — but deliberately NOT the full "how
    /// it went" block: written every few minutes for a long session, the full block repeated each
    /// time reproduces the exact "fourteen near-identical blocks" problem <see cref="Detail"/>'s own
    /// comment already describes, in slow motion. Conor's own instinct, 2026-08-27: only the true end
    /// gets the full report; everything before it earns its place on disk by staying small. Uses
    /// <see cref="Write"/> (timestamped, single line), not <see cref="Append"/> directly, so it reads
    /// exactly like every other line in this file rather than standing out as a different kind of
    /// entry.
    /// </summary>
    public void Checkpoint(string line) => Write($"…still running — {line}");

    /// <summary>
    /// One transfer's own before/during/after/recovery lines, written the instant that transfer ends
    /// — not the whole report, and not every transfer that came before it. Added 2026-08-27: a
    /// session that never gets a clean close (the X button on a machine with something else wrong,
    /// Task Manager, a crash, a plain kill) used to leave nothing on disk at all, which is exactly the
    /// scenario this file exists for — a nervous stranger closes the way they close everything, and
    /// sends whatever is there. Each one is its own block rather than replacing the last: this file is
    /// append-only everywhere else, and rewriting a live line risks losing MORE on a crash mid-write
    /// than the redundancy would. If the session ends cleanly, the real "how it went" block after the
    /// DISCONNECTED line is the one that matters, and these are superseded by it; if it does not, the
    /// last one of these IS the record.
    /// </summary>
    public void TransferCheckpoint(string block) =>
        // block already carries AppendOneTransfer's own indentation on every line — no extra prefix.
        Append($"  -- transfer finished at {DateTime.Now:HH:mm:ss}, session still running --{Environment.NewLine}"
             + block + Environment.NewLine);

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
                LastWriteError = null;
            }
            catch (Exception ex)
            {
                // A log that cannot be written must never take the session down — that part was
                // always right. What was wrong was doing NOTHING else: silence here is
                // indistinguishable from nothing having happened, which is exactly the failure mode
                // that let a real three-hour session vanish with no record and no clue why. See
                // PROGRESS.md, 2026-08-27. The session still does not stop; the failure just stops
                // being invisible.
                LastWriteError = ex.Message;
                LastWriteErrorAtUtc = DateTime.UtcNow;
                try { WriteFailed?.Invoke(ex.Message); } catch { /* a subscriber's own failure is not this file's problem */ }
            }
        }
    }
}

namespace RemoteDesktop.Host.Identity;

/// <summary>
/// A plain text record, on the client's own machine, of every connection this computer was asked
/// for: who asked, when, and what was decided. Hard rule 4 in CLAUDE.md §4 — the person must be
/// able to open a file and see who has been on their machine.
///
/// It records WHO and WHEN and nothing else. No screen content, no keystrokes, no file names —
/// the log answers "did someone connect, and who" and deliberately cannot answer anything more.
/// It never leaves this machine.
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

    /// <summary>
    /// Appends the block SessionRecorder produced, unstamped and indented under the DISCONNECTED
    /// line it belongs to. This is how a tester reports a session: they send this file instead of
    /// reading numbers off a screen while someone talks to them on the phone.
    ///
    /// It stays within the promise the rest of this file makes — it describes the CONNECTION (how
    /// fast, how steady, how much data), never anything that was on the screen.
    /// </summary>
    public void Detail(string block) => Append(block + Environment.NewLine);

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

using System.Security.Cryptography;

namespace RemoteDesktop.Shared.Files;

/// <summary>
/// Keeps a half-finished transfer from becoming rubbish on somebody's disk.
///
/// <para><b>THE PROBLEM.</b> A transfer writes to a temporary name and renames only when the last
/// byte has arrived and the size matches — that is what stops a half file ever wearing the real
/// name. But a hard drop (the link dies, the machine loses power, the person closes the window
/// mid-transfer) leaves the temporary file behind, in the client's own folder, and nothing ever
/// comes back for it. Do that a few times and a stranger who trusted us has a folder full of
/// litter they did not put there and cannot explain.</para>
///
/// <para><b>WHY THE PARTIAL LIVES IN THE DESTINATION FOLDER, not in a FlashDesk temp folder.</b>
/// It has to be renamed into place at the end, and a rename is only instant and only atomic inside
/// one volume. A partial kept in <c>%APPDATA%</c> and finished onto <c>D:\</c> would turn the last
/// step into a full second copy of the file: twice the writing, a fresh chance to run out of disk
/// at the very moment the transfer looked complete, and a long window where the bytes exist under a
/// name nothing has recorded. So the partial sits beside its destination — and this class is what
/// remembers it, because the folder itself is chosen per upload and cannot be guessed at start-up.
/// </para>
///
/// <para><b>THE LEDGER, and why it is written BEFORE the file.</b> Every partial is recorded in one
/// plain text file next to the identity, one path per line, and the line is written before the file
/// is created. The other order looks tidier and loses exactly the case this exists for: a crash
/// between creating the file and recording it leaves a partial nothing knows about.</para>
///
/// <para><b>DELETING IS THE DANGEROUS HALF, so it is guarded twice.</b> A path is removed only if
/// its file name still carries BOTH our prefix and our suffix. That guard is not about tidiness: a
/// ledger that was corrupted, hand-edited, or written by something hostile must not be able to
/// name <c>C:\Windows\System32\kernel32.dll</c> and have this class delete it. Nothing outside the
/// naming pattern is ever touched, whatever the ledger says.</para>
///
/// <para><b>A live transfer cannot be swept away by a second copy starting.</b> The writing side
/// opens its partial with <c>FileShare.None</c>, so Windows itself refuses the delete; the entry
/// simply stays in the ledger for next time. That is why the file share matters and is stated
/// here rather than left to the caller to remember.</para>
/// </summary>
public sealed class PartialFiles
{
    /// <summary>
    /// What every unfinished transfer is called while it is unfinished. Both halves are checked
    /// before anything is deleted, so an ordinary file would have to be named to imitate us on
    /// purpose to be at risk — and even then it would have to be listed in our own ledger.
    /// </summary>
    public const string NamePrefix = "FlashDesk-incoming-";
    public const string NameSuffix = ".flashdesk-part";

    /// <summary>
    /// The ledger's name. Deliberately says what it is in words, because it sits in the same folder
    /// a curious person opens to find sessions.txt.
    /// </summary>
    public const string LedgerFileName = "unfinished-files.txt";

    /// <summary>
    /// Most lines read back from the ledger. A file that has grown past this is not a ledger any
    /// more, and walking it would delay the window opening for no benefit.
    /// </summary>
    private const int MaxLedgerLines = 10_000;

    private readonly object _gate = new();
    private readonly string _folder;
    private readonly string _ledger;

    /// <param name="folder">Where FlashDesk keeps what it remembers on this machine — the same
    /// folder as identity.json and sessions.txt.</param>
    public PartialFiles(string folder)
    {
        _folder = folder;
        _ledger = Path.Combine(folder, LedgerFileName);
    }

    public string LedgerPath => _ledger;

    /// <summary>A fresh temporary name. Random, so two transfers into one folder cannot collide.</summary>
    public static string NewName() =>
        NamePrefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) + NameSuffix;

    /// <summary>
    /// True only for a name this class produced. Both ends are required; a file called
    /// <c>holiday.flashdesk-part</c> is somebody else's and is never deleted.
    /// </summary>
    public static bool IsPartialName(string? fileName) =>
        fileName is not null
        && fileName.Length > NamePrefix.Length + NameSuffix.Length
        && fileName.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)
        && fileName.EndsWith(NameSuffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Records a partial and returns the full path to write to. THE CALLER CREATES THE FILE, after
    /// this returns — that order is the whole point of the ledger.
    /// </summary>
    /// <param name="destinationFolder">The folder the finished file will end up in. The partial is
    /// written here, so the finishing rename stays inside one volume.</param>
    public string Begin(string destinationFolder)
    {
        string path = Path.Combine(destinationFolder, NewName());
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(_ledger, path + Environment.NewLine);
            }
            catch
            {
                // An unwritable ledger must not stop a transfer the person agreed to. What is lost
                // is the cleanup of THIS file if the link then dies — worse than recording it,
                // better than refusing a support session because a text file would not open.
            }
        }
        return path;
    }

    /// <summary>
    /// This partial is dealt with — renamed into place, or deleted after a failure. Forget it, so a
    /// later start does not go looking for a file that is not there.
    /// </summary>
    public void Finished(string tempPath)
    {
        lock (_gate)
        {
            var keep = new List<string>();
            foreach (string line in ReadLedger())
                if (!line.Equals(tempPath, StringComparison.OrdinalIgnoreCase))
                    keep.Add(line);

            WriteLedger(keep);
        }
    }

    /// <summary>What a start-up sweep did, for the log and for the technical view.</summary>
    /// <param name="Removed">Partials deleted.</param>
    /// <param name="Kept">Partials that could not be deleted — in use, or the disk refused. They
    /// stay in the ledger and are tried again next time rather than being forgotten.</param>
    public readonly record struct Sweep(int Removed, int Kept);

    /// <summary>
    /// Deletes every unfinished transfer this machine recorded and did not finish. Safe to call
    /// when there is no ledger, no folder, and nothing to do — that is the ordinary case.
    /// </summary>
    public Sweep CleanUpOnStart()
    {
        lock (_gate)
        {
            int removed = 0;
            var keep = new List<string>();

            foreach (string path in ReadLedger())
            {
                string name;
                try { name = Path.GetFileName(path); }
                catch { continue; } // unreadable as a path: forget it, never act on it

                // THE GUARD. Anything not wearing our own name is dropped from the ledger without
                // being touched. A ledger is not permission to delete.
                if (!IsPartialName(name)) continue;

                try
                {
                    if (!File.Exists(path)) continue; // already gone: nothing to do, nothing to keep
                    File.Delete(path);
                    removed++;
                }
                catch
                {
                    // Locked by a copy that is still transferring it, or the disk said no. Keep the
                    // line so the next start tries again instead of leaving it forever.
                    keep.Add(path);
                }
            }

            WriteLedger(keep);
            return new Sweep(removed, keep.Count);
        }
    }

    private List<string> ReadLedger()
    {
        var lines = new List<string>();
        try
        {
            if (!File.Exists(_ledger)) return lines;

            foreach (string raw in File.ReadLines(_ledger))
            {
                if (lines.Count >= MaxLedgerLines) break;
                string line = raw.Trim();
                if (line.Length > 0) lines.Add(line);
            }
        }
        catch
        {
            // An unreadable ledger means we cannot clean up this time. It must never be the reason
            // the program does not open.
        }
        return lines;
    }

    private void WriteLedger(List<string> lines)
    {
        try
        {
            if (lines.Count == 0)
            {
                if (File.Exists(_ledger)) File.Delete(_ledger);
                return;
            }

            Directory.CreateDirectory(_folder);
            File.WriteAllLines(_ledger, lines);
        }
        catch { /* same reasoning as ReadLedger */ }
    }
}

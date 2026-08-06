using System.Text;
using RemoteDesktop.Shared.Files;

namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// Checks that the program's own housekeeping still does what it claims, and answers PASS or FAIL.
///
/// <para><b>WHY THIS IS IN THE PRODUCT AND NOT ONLY IN THE UNIT TESTS.</b> The unit tests prove the
/// code was right on the machine that built it. They cannot be run by the person who directs this
/// project, and they say nothing about the machine the program is actually running on — a different
/// disk, a different Windows account, a folder the program may not be allowed to write to. This runs
/// the real <see cref="PartialFiles"/> against a real folder on THIS machine and reports in words.
/// It exists so that "does the cleanup work?" is answered by the program rather than by a person
/// typing commands into PowerShell and interpreting the result.</para>
///
/// <para><b>It never touches anything the program did not create.</b> Everything happens inside one
/// freshly-made folder under the system temporary directory, which is removed at the end whether the
/// check passed or failed. The real ledger, the real identity and the real session log are not read
/// and not written.</para>
///
/// <para><b>It is not run automatically at start-up</b>, and that is deliberate: it writes files, and
/// writing files on a stranger's disk every time they open the program — to prove something to
/// somebody who is not them — is not a cost the client should pay. It runs when it is asked for.</para>
/// </summary>
public static class SelfTest
{
    /// <summary>The outcome, in the two forms a caller needs: a verdict and something to read.</summary>
    public readonly record struct Result(bool Passed, int Checks, int Failures, string Report)
    {
        /// <summary>One line for a window, where there is no room for the whole report.</summary>
        public string Summary => Passed
            ? $"PASS - all {Checks} checks passed."
            : $"FAIL - {Failures} of {Checks} checks failed.";
    }

    /// <summary>
    /// Runs every self-check there is. Today that is the unfinished-file cleanup; anything added
    /// later goes here so there stays exactly one thing to press.
    /// </summary>
    public static Result Run()
    {
        var report = new StringBuilder();
        report.AppendLine("FlashDesk self-test");
        report.AppendLine("===================");
        report.AppendLine($"Time    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Machine : {Environment.MachineName}");
        report.AppendLine();
        report.AppendLine("Unfinished-file cleanup - what a transfer that was cut off leaves behind,");
        report.AppendLine("and, far more importantly, what must never be deleted.");
        report.AppendLine();

        var checks = new List<(string What, bool Passed, string Detail)>();
        string root = Path.Combine(Path.GetTempPath(), "FlashDesk-selftest-" + Guid.NewGuid().ToString("N"));

        try
        {
            string config = Path.Combine(root, "config");
            string destination = Path.Combine(root, "Downloads");
            Directory.CreateDirectory(config);
            Directory.CreateDirectory(destination);

            checks.Add(CutOffTransferIsRemoved(config, destination));
            checks.Add(AnOrdinaryFileIsNeverDeleted(config, destination));
            checks.Add(AnUnrecordedFileIsNeverDeleted(config, destination));
            checks.Add(AFinishedTransferSurvives(config, destination));
            checks.Add(AFileStillBeingWrittenSurvives(config, destination));
        }
        catch (Exception ex)
        {
            checks.Add(("the check could run at all", false, ex.GetType().Name + ": " + ex.Message));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }
        }

        foreach (var (what, passed, detail) in checks)
        {
            report.AppendLine($"  [{(passed ? "PASS" : "FAIL")}]  {what}");
            if (detail.Length > 0) report.AppendLine($"          {detail}");
        }

        int failures = checks.Count(c => !c.Passed);
        report.AppendLine();
        report.AppendLine(failures == 0
            ? $"RESULT: PASS - all {checks.Count} checks passed."
            : $"RESULT: FAIL - {failures} of {checks.Count} checks failed.");

        if (failures > 0)
        {
            report.AppendLine();
            report.AppendLine("A FAIL here means an interrupted file transfer may leave a file behind on this");
            report.AppendLine("machine, or - much worse if it is one of the three 'never deleted' checks - that");
            report.AppendLine("something which is not ours could be removed. Do not use file transfer until it");
            report.AppendLine("passes.");
        }

        return new Result(failures == 0, checks.Count, failures, report.ToString());
    }

    // ---- the checks. Each one makes its own mess and asserts on the world, not on a return value.

    private static (string, bool, string) CutOffTransferIsRemoved(string config, string destination)
    {
        var files = new PartialFiles(config);
        string partial = files.Begin(destination);
        File.WriteAllBytes(partial, new byte[4096]);   // the link died here

        var sweep = new PartialFiles(config).CleanUpOnStart();  // a later run of the program

        bool passed = sweep.Removed == 1 && !File.Exists(partial);
        return ("an interrupted transfer is removed on the next start", passed,
            passed ? "" : $"removed {sweep.Removed}, file still there: {File.Exists(partial)}");
    }

    private static (string, bool, string) AnOrdinaryFileIsNeverDeleted(string config, string destination)
    {
        // The row that matters most: a ledger that names something which is not ours. Corrupted,
        // hand-edited, or written by something hostile - it must change nothing.
        string innocent = Path.Combine(destination, "important-document.docx");
        File.WriteAllText(innocent, "somebody's actual work");
        File.WriteAllLines(Path.Combine(config, PartialFiles.LedgerFileName), new[] { innocent });

        var sweep = new PartialFiles(config).CleanUpOnStart();

        bool passed = sweep.Removed == 0 && File.Exists(innocent);
        TryDelete(innocent);
        return ("a ledger naming an ordinary file deletes nothing", passed,
            passed ? "" : "A FILE THAT WAS NOT OURS WAS DELETED.");
    }

    private static (string, bool, string) AnUnrecordedFileIsNeverDeleted(string config, string destination)
    {
        // Cleanup follows the ledger, never the folder. Sweeping by pattern would mean deleting on
        // evidence we did not write.
        string stray = Path.Combine(destination, PartialFiles.NewName());
        File.WriteAllText(stray, "not in the ledger");

        var sweep = new PartialFiles(config).CleanUpOnStart();

        bool passed = sweep.Removed == 0 && File.Exists(stray);
        TryDelete(stray);
        return ("a file that was never recorded is left alone", passed,
            passed ? "" : "AN UNRECORDED FILE WAS DELETED.");
    }

    private static (string, bool, string) AFinishedTransferSurvives(string config, string destination)
    {
        var files = new PartialFiles(config);
        string partial = files.Begin(destination);
        File.WriteAllBytes(partial, new byte[16]);

        string landed = Path.Combine(destination, "Report.docx");
        File.Move(partial, landed);      // what a completed transfer does
        files.Finished(partial);

        var sweep = new PartialFiles(config).CleanUpOnStart();

        bool passed = sweep.Removed == 0 && File.Exists(landed) && !File.Exists(files.LedgerPath);
        TryDelete(landed);
        return ("a completed transfer survives and leaves no ledger", passed,
            passed ? "" : $"removed {sweep.Removed}, file there: {File.Exists(landed)}");
    }

    private static (string, bool, string) AFileStillBeingWrittenSurvives(string config, string destination)
    {
        // A second copy of FlashDesk starting mid-transfer must not sweep away the file the first
        // one is still writing. FileShare.None is what makes Windows itself refuse the delete.
        var files = new PartialFiles(config);
        string partial = files.Begin(destination);

        bool survivedWhileOpen;
        using (new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var during = new PartialFiles(config).CleanUpOnStart();
            survivedWhileOpen = during.Removed == 0 && during.Kept == 1 && File.Exists(partial);
        }

        // ...and once the handle is closed, the next start finishes the job rather than forgetting.
        var after = new PartialFiles(config).CleanUpOnStart();

        bool passed = survivedWhileOpen && after.Removed == 1 && !File.Exists(partial);
        TryDelete(partial);
        return ("a transfer still in progress is not swept away, and is cleaned up afterwards", passed,
            passed ? "" : $"survived while open: {survivedWhileOpen}, removed afterwards: {after.Removed}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}

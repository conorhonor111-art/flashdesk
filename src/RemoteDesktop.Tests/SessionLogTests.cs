using RemoteDesktop.Host.Identity;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// Proves SessionLog.Append's failure path is now VISIBLE rather than silent. Built 2026-08-27 after
/// a real three-hour session vanished from sessions.txt with no trace anywhere: the old catch
/// swallowed the exception completely, so "the write failed" and "nothing happened" were
/// indistinguishable from outside the class. This does not reproduce THAT specific failure — its
/// exact cause was never recovered, see PROGRESS.md — it proves the new reporting mechanism itself
/// works: a genuinely broken write path fires <see cref="SessionLog.WriteFailed"/> and sets
/// <see cref="SessionLog.LastWriteError"/>, and a subsequent working write clears it again.
///
/// Both failures here are real ones a write can hit on a real disk (an illegal path character, a
/// file occupying where a folder needs to be) — not a mock standing in for "something went wrong".
/// </summary>
public class SessionLogTests
{
    [Fact]
    public void A_write_that_cannot_reach_disk_is_reported_not_swallowed()
    {
        // A NUL character makes every Windows path operation throw — a real, reproducible way to
        // force Append() down its catch branch without touching anything else on the test disk.
        var log = new SessionLog(Path.GetTempPath() + "\0bad");

        string? seen = null;
        log.WriteFailed += message => seen = message;

        log.Started("123456789");

        Assert.NotNull(log.LastWriteError);
        Assert.NotNull(log.LastWriteErrorAtUtc);
        Assert.NotNull(seen);
        Assert.Equal(log.LastWriteError, seen);
    }

    [Fact]
    public void A_working_write_clears_a_previous_failure_on_the_same_instance()
    {
        string root = Path.Combine(Path.GetTempPath(), "flashdesk-tests-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "config");
        try
        {
            Directory.CreateDirectory(root);
            // A FILE sitting exactly where the folder needs to be: Directory.CreateDirectory cannot
            // succeed while it is there.
            File.WriteAllText(folder, "not a directory");

            var log = new SessionLog(folder);
            log.Started("123456789");
            Assert.NotNull(log.LastWriteError);

            // Clear the obstruction and let the SAME instance try again — self-correcting is the
            // point: the technical view's live line must not stay stuck on a failure that ended.
            File.Delete(folder);
            log.Ended("123456789");

            // LastWriteError clears the moment a write succeeds — that is what the technical view's
            // live line reads. LastWriteErrorAtUtc deliberately stays as a historical "when did this
            // last happen", per its own doc comment; a caller who wants "and it is fine now" reads
            // LastWriteError alone, as UpdateStatus() does.
            Assert.Null(log.LastWriteError);
            Assert.True(File.Exists(log.FilePath));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void An_ordinary_write_never_raises_WriteFailed()
    {
        string root = Path.Combine(Path.GetTempPath(), "flashdesk-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new SessionLog(root);
            bool fired = false;
            log.WriteFailed += _ => fired = true;

            log.Started("123456789");
            log.Checkpoint("2m so far, 12.0 fps avg, 4.2 KB/s avg, level 0 now (reached 3 at worst), 1 file transfer(s)");
            log.TransferCheckpoint("                     #1  10:00:00.000 to 10:00:05.000 (5.0s)");
            log.Ended("123456789");

            Assert.False(fired);
            Assert.Null(log.LastWriteError);
            string text = File.ReadAllText(log.FilePath);
            Assert.Contains("still running", text);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}

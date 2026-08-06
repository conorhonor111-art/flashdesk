using RemoteDesktop.Shared.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The start-up sweep, tested against a real folder on a real disk rather than a mock, because the
/// thing being proved is a file being deleted and a different file NOT being deleted — and a fake
/// file system would only prove that the fake agrees with the code.
///
/// The rows that matter most are the ones where nothing should happen. A cleanup that deletes too
/// much is far worse than one that leaves litter: the litter is our own file with a meaningless
/// name, and the alternative is somebody's document.
/// </summary>
public class PartialFilesTests : IDisposable
{
    private readonly string _config;
    private readonly string _destination;

    public PartialFilesTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "flashdesk-tests-" + Guid.NewGuid().ToString("N"));
        _config = Path.Combine(root, "config");
        _destination = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(_config);
        Directory.CreateDirectory(_destination);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_config)!, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private PartialFiles New() => new(_config);

    // ------------------------------------------------------------------ the naming guard

    [Theory]
    [InlineData("FlashDesk-incoming-0011223344556677.flashdesk-part", true)]
    [InlineData("flashdesk-incoming-AABB.FLASHDESK-PART", true)]      // Windows is case-insensitive
    [InlineData("holiday.flashdesk-part", false)]                     // right suffix, not ours
    [InlineData("FlashDesk-incoming-report.docx", false)]             // right prefix, not ours
    [InlineData("FlashDesk-incoming-.flashdesk-part", false)]         // both ends, nothing between
    [InlineData("kernel32.dll", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_our_own_naming_counts_as_a_partial(string? name, bool expected) =>
        Assert.Equal(expected, PartialFiles.IsPartialName(name));

    [Fact]
    public void A_new_name_carries_both_ends_and_is_never_the_same_twice()
    {
        string a = PartialFiles.NewName();
        string b = PartialFiles.NewName();

        Assert.True(PartialFiles.IsPartialName(a));
        Assert.True(PartialFiles.IsPartialName(b));
        Assert.NotEqual(a, b);
    }

    // ------------------------------------------------------------------ the ledger

    [Fact]
    public void Begin_records_the_path_BEFORE_the_file_exists()
    {
        string path = New().Begin(_destination);

        // The whole point of the order: nothing has been created yet, and it is already recorded.
        Assert.False(File.Exists(path));
        Assert.Contains(path, File.ReadAllLines(Path.Combine(_config, PartialFiles.LedgerFileName)));
    }

    [Fact]
    public void Begin_puts_the_partial_in_the_destination_folder()
    {
        // Because the finishing rename has to stay inside one volume - see the class comment.
        Assert.Equal(_destination, Path.GetDirectoryName(New().Begin(_destination)));
    }

    [Fact]
    public void A_partial_left_by_a_hard_drop_is_deleted_on_the_next_start()
    {
        var files = New();
        string path = files.Begin(_destination);
        File.WriteAllBytes(path, new byte[4096]); // the transfer got this far, then the link died

        var sweep = New().CleanUpOnStart(); // a later run of the program

        Assert.Equal(1, sweep.Removed);
        Assert.Equal(0, sweep.Kept);
        Assert.False(File.Exists(path));
        Assert.Empty(Directory.GetFiles(_destination));
    }

    [Fact]
    public void A_finished_transfer_is_not_swept_and_leaves_no_ledger_behind()
    {
        var files = New();
        string path = files.Begin(_destination);
        File.WriteAllBytes(path, new byte[16]);

        string final = Path.Combine(_destination, "Report.docx");
        File.Move(path, final);          // what a completed transfer does
        files.Finished(path);

        var sweep = New().CleanUpOnStart();

        Assert.Equal(0, sweep.Removed);
        Assert.True(File.Exists(final)); // the real file survives untouched
        Assert.False(File.Exists(files.LedgerPath));
    }

    // ------------------------------------------------------------------ what must NEVER be deleted

    [Fact]
    public void A_ledger_naming_an_ordinary_file_deletes_nothing()
    {
        // The row this class exists to fail safely on: a corrupted, hand-edited or hostile ledger.
        string innocent = Path.Combine(_destination, "kernel32.dll");
        File.WriteAllText(innocent, "not ours");
        File.WriteAllLines(Path.Combine(_config, PartialFiles.LedgerFileName), new[] { innocent });

        var sweep = New().CleanUpOnStart();

        Assert.Equal(0, sweep.Removed);
        Assert.True(File.Exists(innocent));
    }

    [Fact]
    public void A_file_that_merely_ends_like_ours_is_left_alone()
    {
        string theirs = Path.Combine(_destination, "holiday.flashdesk-part");
        File.WriteAllText(theirs, "somebody else's");
        File.WriteAllLines(Path.Combine(_config, PartialFiles.LedgerFileName), new[] { theirs });

        Assert.Equal(0, New().CleanUpOnStart().Removed);
        Assert.True(File.Exists(theirs));
    }

    [Fact]
    public void An_unrecorded_partial_is_left_alone()
    {
        // Cleanup follows the ledger, never the folder. Sweeping a folder by pattern would mean
        // walking somebody's whole disk deciding what to delete, on evidence we did not write.
        string stray = Path.Combine(_destination, PartialFiles.NewName());
        File.WriteAllText(stray, "not in the ledger");

        Assert.Equal(0, New().CleanUpOnStart().Removed);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public void A_partial_still_being_written_survives_and_is_tried_again()
    {
        var files = New();
        string path = files.Begin(_destination);

        // FileShare.None is how the writing side opens it, and it is what makes a second copy of
        // FlashDesk starting mid-transfer harmless.
        using (new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            var sweep = New().CleanUpOnStart();
            Assert.Equal(0, sweep.Removed);
            Assert.Equal(1, sweep.Kept);
            Assert.True(File.Exists(path));
        }

        // The handle is closed now, so the next start finishes the job.
        Assert.Equal(1, New().CleanUpOnStart().Removed);
    }

    // ------------------------------------------------------------------ it must never stop start-up

    [Fact]
    public void No_ledger_and_no_folder_is_the_ordinary_case_and_does_nothing()
    {
        var sweep = new PartialFiles(Path.Combine(_config, "never-created")).CleanUpOnStart();
        Assert.Equal(0, sweep.Removed);
        Assert.Equal(0, sweep.Kept);
    }

    [Fact]
    public void Junk_in_the_ledger_is_skipped_and_the_real_entries_still_go()
    {
        var files = New();
        string path = files.Begin(_destination);
        File.WriteAllBytes(path, new byte[8]);

        File.AppendAllLines(files.LedgerPath, new[]
        {
            "",
            "   ",
            "not a path at all |<>",
            "C:\\",
        });

        var sweep = New().CleanUpOnStart();

        Assert.Equal(1, sweep.Removed);
        Assert.False(File.Exists(path));
    }
}

using RemoteDesktop.Host.Diagnostics;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The self-test, tested. That is not circular: these prove the CHECKER is honest — that it really
/// runs its checks, really reports a verdict, and really cleans up after itself. What it then
/// asserts about <c>PartialFiles</c> is proved separately in <see cref="PartialFilesTests"/>.
///
/// The one that matters is the last: a checker that leaves its own litter behind while proving that
/// litter gets cleaned up would be its own counter-example.
/// </summary>
public class SelfTestTests
{
    [Fact]
    public void The_self_test_passes_on_this_machine()
    {
        var result = SelfTest.Run();

        Assert.True(result.Passed, result.Report);
        Assert.True(result.Checks >= 5, $"only {result.Checks} checks ran.");
        Assert.Equal(0, result.Failures);
    }

    [Fact]
    public void The_verdict_is_a_word_a_person_can_read_without_the_report()
    {
        var result = SelfTest.Run();

        Assert.Contains("PASS", result.Summary);
        Assert.Contains("RESULT: PASS", result.Report);
        // Every check names what it checked, so a FAIL says which one rather than just "failed".
        Assert.Contains("[PASS]", result.Report);
    }

    [Fact]
    public void A_failure_says_FAIL_and_says_how_many()
    {
        // The path that must not be silent. A checker whose failure looks like nothing happened is
        // worse than no checker, because it reads as a pass.
        var failed = new SelfTest.Result(false, 5, 2, "");

        Assert.StartsWith("FAIL", failed.Summary);
        Assert.Contains("2 of 5", failed.Summary);
    }

    [Fact]
    public void It_leaves_nothing_behind_on_the_disk()
    {
        var before = Directory.GetDirectories(Path.GetTempPath(), "FlashDesk-selftest-*");
        SelfTest.Run();
        var after = Directory.GetDirectories(Path.GetTempPath(), "FlashDesk-selftest-*");

        Assert.Equal(before.Length, after.Length);
    }
}

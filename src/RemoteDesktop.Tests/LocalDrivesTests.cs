using RemoteDesktop.Host.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The drive half of the path rule.
///
/// <para><b>⚠ WHAT THESE TESTS DO NOT COVER, stated plainly rather than left to be assumed: the
/// mapped network drive.</b> That is the case this file exists for — <c>\\fileserver\finance</c>
/// mapped to <c>Z:</c> passes every string rule, because by then it is spelled like an ordinary
/// local path, and the person in front of us cannot consent for their employer's server. Proving it
/// needs a real network share and an administrator to map it, neither of which a unit test has. So
/// the refusal rests on <c>DriveType.Network</c> not being in the allow list, which is read here by
/// eye and not by assertion. If a share is ever available, this is the first thing to test on it.
/// </para>
/// </summary>
public class LocalDrivesTests
{
    [Fact]
    public void This_machines_own_system_drive_is_browsable()
    {
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.True(LocalDrives.IsOnLocalDrive(windows, out string? problem), problem);
        Assert.Null(problem);
    }

    [Fact]
    public void A_drive_letter_that_is_not_there_is_refused_with_a_sentence()
    {
        // Not "false and nothing else": a blank message surfaces as an empty box and reads as a
        // crash, so every refusal in this feature carries words.
        Assert.False(LocalDrives.IsOnLocalDrive(@"Q:\somewhere", out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notapath")]
    public void Something_that_is_not_a_rooted_path_is_refused(string path)
    {
        Assert.False(LocalDrives.IsOnLocalDrive(path, out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Fact]
    public void The_root_listing_is_drives_this_machine_actually_has()
    {
        var roots = LocalDrives.Roots();

        Assert.NotEmpty(roots);
        Assert.All(roots, r => Assert.True(r.IsDirectory));
        Assert.All(roots, r => Assert.True(Directory.Exists(r.Name), $"{r.Name} was listed but is not there."));
        // Every drive offered at the root must itself pass the check that guards every later path,
        // or the browser would open with a row that refuses to be clicked.
        Assert.All(roots, r => Assert.True(LocalDrives.IsOnLocalDrive(r.Name, out _), $"{r.Name} was listed but is refused."));
    }
}

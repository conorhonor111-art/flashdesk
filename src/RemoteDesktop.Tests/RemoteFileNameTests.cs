using RemoteDesktop.Shared.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The WRITE side of path safety, written before the upload it protects.
///
/// Reading and writing are not the same risk and are not checked the same way. Reading is allowed
/// across the whole drive, so a path only has to be a real place on this machine. Writing is
/// confined to ONE folder the operator chose, because writing is where damage is permanent — and
/// that confinement is only as good as two things: the filename is a bare name, and the check runs
/// on the RESOLVED path rather than on the string that was joined.
/// </summary>
public class RemoteFileNameTests
{
    // ---------------------------------------------------------------- bare names

    [Theory]
    // A separator turns "a name" into "a path", and the file lands somewhere else entirely.
    [InlineData(@"..\..\Windows\System32\evil.dll")]
    [InlineData(@"sub\file.txt")]
    [InlineData("sub/file.txt")]
    [InlineData("..")]
    [InlineData(".")]
    // A drive letter escapes the folder without a single separator.
    [InlineData(@"C:evil.txt")]
    [InlineData(@"C:\evil.txt")]
    // An alternate data stream writes bytes nobody can see in Explorer.
    [InlineData("report.txt:hidden")]
    // Reserved device names.
    [InlineData("CON")]
    [InlineData("NUL.txt")]
    [InlineData("COM1")]
    // ⚠ Windows SILENTLY STRIPS a trailing dot or space, so "evil.exe." becomes "evil.exe" AFTER
    // any check that saw a different name. Refused rather than normalised: a name that changes
    // between being checked and being used is exactly the shape of a bypass.
    [InlineData("evil.exe.")]
    [InlineData("evil.exe ")]
    [InlineData("report.txt.")]
    // Malformed.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0name.txt")]
    public void Rejects_as_a_file_name(string name)
        => Assert.False(RemotePath.IsSafeFileName(name, out _));

    [Fact]
    public void Rejects_a_name_longer_than_windows_allows()
        => Assert.False(RemotePath.IsSafeFileName(new string('a', 300), out _));

    [Theory]
    [InlineData("report.txt")]
    [InlineData("Invoice March 2026.pdf")]
    [InlineData("driver-setup.exe")]
    [InlineData("отчёт.docx")]
    [InlineData("ანგარიში.pdf")]
    [InlineData("a.b.c.tar.gz")]
    [InlineData("no-extension")]
    public void Accepts_as_a_file_name(string name)
        => Assert.True(RemotePath.IsSafeFileName(name, out _));

    [Fact]
    public void Says_why_it_refused()
    {
        Assert.False(RemotePath.IsSafeFileName(@"sub\file.txt", out string? why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    // ---------------------------------------------------------------- confinement

    [Fact]
    public void Builds_a_destination_inside_the_chosen_folder()
    {
        Assert.True(RemotePath.TryResolveInside(@"C:\Users\Ann\Documents", "report.txt", out string? full, out _));
        Assert.Equal(@"C:\Users\Ann\Documents\report.txt", full);
    }

    [Fact]
    public void A_sibling_folder_with_the_same_prefix_is_NOT_inside()
    {
        // The classic prefix bug: "C:\Temp2" starts with "C:\Temp", so a check written as
        // StartsWith would let a file be written into a folder the operator never chose. The
        // separator is what makes it a containment test rather than a string test.
        Assert.False(RemotePath.TryResolveInside(@"C:\Temp", @"..\Temp2\x.txt", out _, out _));
    }

    [Fact]
    public void The_check_runs_on_the_resolved_path_not_the_joined_string()
    {
        // Joining "C:\Temp" with "..\..\Windows\x.dll" produces a string that still begins with
        // C:\Temp. Only resolving it first reveals that it lands in C:\Windows.
        Assert.False(RemotePath.TryResolveInside(@"C:\Temp", @"..\..\Windows\x.dll", out _, out _));
    }

    [Fact]
    public void A_folder_that_is_not_a_usable_path_is_refused()
    {
        Assert.False(RemotePath.TryResolveInside(@"\\server\share", "x.txt", out _, out _));
        Assert.False(RemotePath.TryResolveInside("", "x.txt", out _, out _));
    }

    [Fact]
    public void A_trailing_separator_on_the_folder_does_not_change_the_answer()
    {
        Assert.True(RemotePath.TryResolveInside(@"C:\Users\Ann\Documents\", "report.txt", out string? a, out _));
        Assert.True(RemotePath.TryResolveInside(@"C:\Users\Ann\Documents", "report.txt", out string? b, out _));
        Assert.Equal(a, b);
    }

    // ---------------------------------------------------------------- executables

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("SETUP.EXE", true)]
    [InlineData("run.bat", true)]
    [InlineData("script.cmd", true)]
    [InlineData("thing.msi", true)]
    [InlineData("go.ps1", true)]
    [InlineData("lib.dll", true)]
    [InlineData("shortcut.lnk", true)]
    [InlineData("report.pdf", false)]
    [InlineData("photo.jpg", false)]
    [InlineData("notes.txt", false)]
    public void Recognises_a_program_so_the_person_can_be_told_what_is_arriving(string name, bool expected)
        => Assert.Equal(expected, RemotePath.LooksExecutable(name));
}

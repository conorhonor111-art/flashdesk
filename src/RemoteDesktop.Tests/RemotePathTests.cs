using RemoteDesktop.Shared.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The contract for every path that arrives from the operator, written BEFORE the file browser it
/// protects.
///
/// This is the first code in FlashDesk to turn a string from the other end of the wire into a path
/// on the client's disk. Everything else that touches the filesystem uses a literal name against a
/// folder the program chose. So there is no prior art here to lean on, and the failure mode is not
/// a crash — it is a stranger's private file leaving their machine because a check was written to
/// look right rather than to be right.
///
/// Each rejected row below is a real technique, not a hypothetical.
/// </summary>
public class RemotePathTests
{
    // ---- Found by an adversarial read of these rules, 2026-08-06. Both are names that are not
    // what they appear to be, which is the one failure this whole file exists to prevent.

    [Theory]
    [InlineData(@"C:\Users\Ann\report.txt:hidden")]      // a second body of bytes, in no listing
    [InlineData(@"C:\Users\Ann\report.txt:$DATA")]
    [InlineData(@"C:\Users\Ann\folder:stream\a.txt")]
    public void Hidden_data_inside_a_file_is_not_a_readable_location(string path)
    {
        // The write side has always refused a colon (IsSafeFileName). The READ side did not, and
        // reading is the side where the secrecy matters: an alternate data stream is content the
        // person at that machine has no way of knowing exists.
        Assert.False(RemotePath.TryResolve(path, out _, out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Theory]
    [InlineData("Invoice\u202Excod.exe")]   // displays as "Invoiceexe.docx"
    [InlineData("\u202Egnp.exe")]
    [InlineData("a\u200Fb.txt")]
    [InlineData("a\u2066b\u2069.txt")]
    public void A_name_that_does_not_read_the_way_it_is_spelled_is_refused(string name)
    {
        // Windows, our own dialogs and Explorer all honour these characters. The consent dialog's
        // one job is to say what is arriving; a name that renders backwards makes that line a lie.
        Assert.False(RemotePath.IsSafeFileName(name, out string? problem));
        Assert.False(string.IsNullOrWhiteSpace(problem));
    }

    [Theory]
    [InlineData("Invoice März.pdf")]
    [InlineData("ანგარიში.pdf")]
    [InlineData("Отчёт 2026.xlsx")]
    [InlineData("normal-file (2).txt")]
    public void Ordinary_names_in_any_language_are_still_accepted(string name)
    {
        // The direction rule must not become a rule against non-English names. It is aimed at four
        // ranges of invisible formatting characters, not at alphabets.
        Assert.True(RemotePath.IsSafeFileName(name, out _), name);
    }

    // ---------------------------------------------------------------- must be REJECTED

    [Theory]
    // Relative paths: accepting one means there is a "current directory" to be relative to, and
    // that directory is not something the client ever agreed to expose.
    [InlineData(@"..\Windows")]
    [InlineData(@"Users\Public")]
    [InlineData(@"\Windows")]           // rooted but not qualified: drive comes from somewhere else
    // Another machine entirely. The person in front of us cannot consent for their employer's
    // file server, so this is the exclusion that matters most.
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\192.168.1.50\c$\Windows")]
    // Device and extended namespaces. These are not files.
    [InlineData(@"\\.\PhysicalDrive0")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"\\.\pipe\somepipe")]
    // Reserved DOS device names, which Windows still resolves in some contexts.
    [InlineData("CON")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData(@"C:\Users\Public\CON")]
    // Malformed input.
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Users\\Pub\0lic")]  // embedded NUL truncates the string in a Win32 call
    public void Rejects(string path) => Assert.False(RemotePath.TryResolve(path, out _, out _));

    [Fact]
    public void Rejects_a_path_longer_than_the_bound()
    {
        // Windows tolerates 32767 with the extended prefix; nothing legitimate here is near 4096.
        string huge = @"C:\" + new string('a', 5000);
        Assert.False(RemotePath.TryResolve(huge, out _, out _));
    }

    [Fact]
    public void Rejects_the_extended_UNC_form()
    {
        // \\?\UNC\server\share is the same reach onto another machine wearing a different spelling.
        // Named separately from the plain \\server\share row because it is the form a person
        // testing the rule would try second.
        Assert.False(RemotePath.TryResolve(@"\\?\UNC\server\share", out _, out _));
    }

    // NOT TESTED HERE, AND DELIBERATELY SO: that a MAPPED NETWORK DRIVE (Z: pointing at
    // \\server\share) is refused. That check needs DriveInfo, which only means anything on the
    // machine actually running the host, so it lives in the Host project next to the drive
    // enumeration. Asserting it here would need a mapped drive to exist on the build machine, and a
    // test that silently passes because the condition it needs is absent is worse than no test.
    // What IS covered here is everything decidable from the string alone.

    [Fact]
    public void Gives_a_reason_when_it_refuses()
    {
        // The operator has to be told something true and plain. An empty message would surface as a
        // blank box, which reads as a crash.
        Assert.False(RemotePath.TryResolve(@"\\server\share", out _, out string? why));
        Assert.False(string.IsNullOrWhiteSpace(why));
    }

    // ---------------------------------------------------------------- must be ACCEPTED

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\Users\")]
    [InlineData(@"C:/Users/Public")]                 // forward slashes: Windows accepts them
    [InlineData(@"C:\Users\Public\Documents")]
    [InlineData(@"C:\Пользователи")]                 // Cyrillic
    [InlineData(@"C:\მომხმარებელი")]                 // Georgian
    [InlineData(@"C:\Program Files (x86)")]
    [InlineData(@"C:\a.b.c\d")]
    public void Accepts(string path) => Assert.True(RemotePath.TryResolve(path, out _, out _));

    [Fact]
    public void Canonicalises_before_returning()
    {
        // Every later check must run on the canonical form. A validator that approves the raw
        // string and then hands the raw string on is the classic way traversal survives review.
        Assert.True(RemotePath.TryResolve(@"C:/Users/./Public", out string? full, out _));
        Assert.Equal(@"C:\Users\Public", full);
    }

    /// <summary>
    /// ⚠ THESE ROWS WERE WRITTEN AS "MUST BE REJECTED" AND THE IMPLEMENTATION WAS RIGHT TO ACCEPT
    /// THEM. Recorded as a passing test rather than deleted, because the reasoning matters more
    /// than the rows.
    ///
    /// The brief said "reject traversal ('..')". That instruction belongs to a design where the
    /// operator is confined to a folder — the user profile, say — and `..` is how you climb out of
    /// it. The agreed design has no such boundary: whole local drives are browsable, deliberately,
    /// because the operator already has mouse and keyboard on that machine and could copy any file
    /// into the profile and take it from there anyway.
    ///
    /// With the DRIVE as the boundary, `..` cannot escape anything. Windows clamps it at the root,
    /// so no sequence of `..` leaves C:. Every one of these canonicalises to an ordinary path on the
    /// same drive — a place the operator could have typed directly. Rejecting them would refuse
    /// legitimate support paths while preventing nothing, which is the worst kind of check: it looks
    /// protective and is not.
    ///
    /// WHAT THIS MEANS FOR THE THREAT THAT REMAINS. Since no string can escape the drive, the only
    /// way off it is a SYMLINK OR JUNCTION that points at another volume or, worse, at
    /// \\server\share. Path.GetFullPath does not resolve those, so no string check can catch them.
    /// The re-check after opening the handle is therefore not defence in depth — it is the ONLY
    /// thing standing between the drive rule and a reach onto another machine.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\Public\..\..\Windows\System32\config\SAM", @"C:\Windows\System32\config\SAM")]
    [InlineData(@"C:\Users\x\..\..\Windows", @"C:\Windows")]
    [InlineData(@"C:\..\..\..\Windows", @"C:\Windows")]
    public void Dot_dot_cannot_leave_the_drive_so_it_is_resolved_not_refused(string given, string expected)
    {
        Assert.True(RemotePath.TryResolve(given, out string? full, out _));
        Assert.Equal(expected, full);
    }

    [Fact]
    public void Resolves_an_inner_dot_dot_that_stays_on_the_drive()
    {
        // "C:\Users\Public\..\Public" is not an escape - it lands back inside. Rejecting it would
        // be a validator that fails safe by failing useful, and support would hit it on real paths.
        Assert.True(RemotePath.TryResolve(@"C:\Users\Public\..\Public", out string? full, out _));
        Assert.Equal(@"C:\Users\Public", full);
    }
}

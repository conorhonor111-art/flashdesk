using System.Text;
using RemoteDesktop.Shared.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The disguise check, written before the upload it guards.
///
/// The attack it exists for is two steps and needs no cleverness: rename virus.exe to invoice.pdf,
/// the person is asked "a file is coming" and says yes, then the operator renames it back with the
/// mouse they already have. The consent was answered truthfully and was still wrong, because the
/// question described the name rather than the thing.
/// </summary>
public class ExecutableContentTests
{
    private static byte[] Head(params byte[] bytes) => bytes;

    // ---------------------------------------------------------------- recognised as a program

    [Fact]
    public void An_exe_is_recognised_from_its_first_two_bytes()
    {
        // "MZ" - every Windows executable image begins with it.
        Assert.True(ExecutableContent.Looks(Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0")));
    }

    [Fact]
    public void A_shortcut_is_recognised()
    {
        // Not a program itself, but double-clicking it runs whatever it points at - including
        // something already on the machine. Same question for the person, so the same answer here.
        Assert.True(ExecutableContent.Looks(Head(0x4C, 0x00, 0x00, 0x00, 0x01, 0x14, 0x02, 0x00)));
    }

    // ---------------------------------------------------------------- not a program

    [Theory]
    [InlineData("%PDF-1.7 ")]              // a real PDF
    [InlineData("\u0089PNG\r\n\u001a\n")]  // a PNG
    [InlineData("Dear Ann,\r\n")]          // plain text
    [InlineData("{\"a\": 1}")]             // json
    public void Ordinary_content_is_not_flagged(string head)
        => Assert.False(ExecutableContent.Looks(Encoding.Latin1.GetBytes(head)));

    [Fact]
    public void A_zip_family_file_is_deliberately_NOT_flagged()
    {
        // "PK" is a .jar - and equally every .docx, .xlsx and .odt. Telling someone "a program is
        // coming" about their own Word document would teach them the warning is noise, and a
        // warning people learn to ignore protects nobody. Recorded as a decision, not an oversight.
        Assert.False(ExecutableContent.Looks(Head(0x50, 0x4B, 0x03, 0x04, 0, 0, 0, 0)));
    }

    [Fact]
    public void An_ole_compound_file_is_deliberately_NOT_flagged()
    {
        // .msi wears this header - and so does every legacy .doc and .xls. Same trade as above.
        Assert.False(ExecutableContent.Looks(Head(0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Too_few_bytes_to_judge_is_not_a_program(int count)
        => Assert.False(ExecutableContent.Looks(new byte[count]));

    // ---------------------------------------------------------------- the verdict

    [Fact]
    public void A_program_whose_name_says_so_is_simply_a_program()
    {
        Assert.Equal(ContentVerdict.Program,
            ExecutableContent.Judge("setup.exe", Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0")));
    }

    [Fact]
    public void A_PROGRAM_WEARING_A_DOCUMENT_NAME_IS_THE_CASE_THIS_EXISTS_FOR()
    {
        Assert.Equal(ContentVerdict.ProgramInDisguise,
            ExecutableContent.Judge("invoice.pdf", Encoding.ASCII.GetBytes("MZ\u0090\0\u0003\0\0\0")));
    }

    [Fact]
    public void An_ordinary_file_is_ordinary()
    {
        Assert.Equal(ContentVerdict.OrdinaryFile,
            ExecutableContent.Judge("invoice.pdf", Encoding.ASCII.GetBytes("%PDF-1.7")));
    }

    [Fact]
    public void A_document_named_exe_is_still_treated_as_a_program()
    {
        // The name claims a program and the bytes do not. It is still asked about as a program:
        // the extension is what Windows will act on when someone double-clicks it, whatever is
        // inside. Erring the other way would let ".exe" through on a technicality.
        Assert.Equal(ContentVerdict.Program,
            ExecutableContent.Judge("setup.exe", Encoding.ASCII.GetBytes("Dear Ann,")));
    }

    /// <summary>
    /// ⚠ THE GAP, ASSERTED SO IT CANNOT BE FORGOTTEN. A batch file is plain text with no signature,
    /// so renaming evil.bat to invoice.pdf defeats BOTH checks. Nothing short of refusing all
    /// unknown content would close it. What covers it is the visible record: whatever arrives is
    /// named, sized and logged on the client's own machine. This test exists to state the limit,
    /// not to hide it — if it ever starts failing, the sniffer has grown and this comment is stale.
    /// </summary>
    [Fact]
    public void A_SCRIPT_RENAMED_TO_A_DOCUMENT_IS_NOT_CAUGHT_AND_THAT_IS_KNOWN()
    {
        var batch = Encoding.ASCII.GetBytes("@echo off\r\ndel /f /q C:\\*.*\r\n");
        Assert.False(ExecutableContent.Looks(batch));
        Assert.Equal(ContentVerdict.OrdinaryFile, ExecutableContent.Judge("invoice.pdf", batch));
    }
}

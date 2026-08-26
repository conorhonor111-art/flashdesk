using RemoteDesktop.Shared.Files;
using RemoteDesktop.Shared.Protocol;
using RemoteDesktop.Viewer.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// Escape was believed by three people to already clear the "remote control paused" band and give
/// focus back to the picture. The first real two-machine test found it did nothing at all - the
/// keypress simply went nowhere, silently, which is exactly the class of bug this project treats as
/// worst.
///
/// <para>This does not exercise <c>ProcessCmdKey</c> itself - that needs a real window handle and
/// message pump, the same limitation <see cref="InputSuspendTests"/> documents for
/// <c>InputCapture.Suspend</c>'s release order, and faking it would test the fake. What IS proven is
/// the logic half: that firing the escape path actually raises the event <c>SessionWindow</c> listens
/// for, wiring it to focus the canvas. <c>ProcessCmdKey</c> itself is the standard WinForms pattern
/// for catching a key ahead of whichever child control has focus, and is verified by hand and by the
/// two-machine test.</para>
/// </summary>
public class FilePanelEscapeTests : IDisposable
{
    private readonly string _folder;

    public FilePanelEscapeTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "flashdesk-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Escape_raises_EscapePressed()
    {
        using var files = new ViewerFileClient(new MessageChannel(new MemoryStream()), new PartialFiles(_folder));
        using var panel = new FilePanel(files);

        bool raised = false;
        panel.EscapePressed += () => raised = true;

        panel.RaiseEscapePressed();

        Assert.True(raised);
    }

    [Fact]
    public void Nobody_listening_is_harmless()
    {
        using var files = new ViewerFileClient(new MessageChannel(new MemoryStream()), new PartialFiles(_folder));
        using var panel = new FilePanel(files);

        panel.RaiseEscapePressed(); // must not throw with no subscriber
    }
}

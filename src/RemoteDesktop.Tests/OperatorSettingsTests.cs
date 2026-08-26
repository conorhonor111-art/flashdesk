using RemoteDesktop.Viewer.Files;
using Xunit;

namespace RemoteDesktop.Tests;

/// <summary>
/// The one thing the operator's side remembers across sessions: the last download folder. Tested
/// against a real file on a real disk — a fake store would only prove the fake agrees with itself,
/// and the thing that actually matters is surviving a fresh <see cref="OperatorSettings"/> instance,
/// which is what a real restart does.
/// </summary>
public class OperatorSettingsTests : IDisposable
{
    private readonly string _folder;

    public OperatorSettingsTests()
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
    public void A_fresh_folder_has_nothing_to_remember()
    {
        Assert.Null(new OperatorSettings(_folder).LastDownloadFolder);
    }

    [Fact]
    public void What_is_set_survives_a_new_instance()
    {
        new OperatorSettings(_folder) { LastDownloadFolder = @"C:\Users\Someone\Downloads" };

        var reopened = new OperatorSettings(_folder);
        Assert.Equal(@"C:\Users\Someone\Downloads", reopened.LastDownloadFolder);
    }

    [Fact]
    public void Setting_it_again_overwrites_rather_than_merges()
    {
        var settings = new OperatorSettings(_folder) { LastDownloadFolder = @"C:\First" };
        settings.LastDownloadFolder = @"C:\Second";

        Assert.Equal(@"C:\Second", new OperatorSettings(_folder).LastDownloadFolder);
    }

    [Fact]
    public void A_corrupted_file_is_treated_as_nothing_remembered_not_a_crash()
    {
        File.WriteAllText(Path.Combine(_folder, "operator-settings.json"), "{ not json");

        Assert.Null(new OperatorSettings(_folder).LastDownloadFolder);
    }
}

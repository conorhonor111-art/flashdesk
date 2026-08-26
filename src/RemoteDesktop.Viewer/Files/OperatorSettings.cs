using System.Text.Json;

namespace RemoteDesktop.Viewer.Files;

/// <summary>
/// The one thing the operator's side remembers about itself, across sessions: where the last
/// download was saved. Nothing else — see <see cref="LastDownloadFolder"/> for why uploads are
/// deliberately excluded. Same load/save shape as <c>KnownCallers</c>: never lets a read or write
/// failure take a session down, because forgetting is not worth interrupting anyone for.
/// </summary>
public sealed class OperatorSettings
{
    private readonly string _file;
    private string? _lastDownloadFolder;

    public OperatorSettings(string folder)
    {
        _file = Path.Combine(folder, "operator-settings.json");
        Load();
    }

    /// <summary>
    /// Where downloads land without asking, once one has been chosen. DOWNLOAD only, by design —
    /// an upload's destination is the remote folder the operator has navigated to, which is already
    /// a deliberate choice made moments earlier; a remembered LOCAL folder has no equivalent
    /// meaning for "where should this go on their computer".
    /// </summary>
    public string? LastDownloadFolder
    {
        get => _lastDownloadFolder;
        set { _lastDownloadFolder = value; Save(); }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var doc = JsonSerializer.Deserialize<Data>(File.ReadAllText(_file));
            _lastDownloadFolder = doc?.LastDownloadFolder;
        }
        catch { _lastDownloadFolder = null; }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Data(_lastDownloadFolder)));
            File.Move(tmp, _file, overwrite: true);
        }
        catch { /* failing to remember is not worth interrupting a session for */ }
    }

    private sealed record Data(string? LastDownloadFolder);
}

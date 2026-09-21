using System.Text.Json;

namespace RemoteDesktop.Viewer.Files;

/// <summary>
/// A rolling list of the last <see cref="MaxEntries"/> peer IDs (raw digit strings, no spaces)
/// that this operator connected to successfully, most-recent first. Persisted across sessions
/// so the operator can reconnect to a number without retyping it.
///
/// <para>Stored locally only — the host side is never told about this list. Its existence is
/// invisible to the person being helped.</para>
///
/// <para>Same resilience contract as <see cref="OperatorSettings"/>: a read or write failure is
/// swallowed so a history glitch never interrupts a session.</para>
/// </summary>
public sealed class ConnectionHistory
{
    public const int MaxEntries = 10;

    private readonly string _file;
    private readonly List<string> _entries = new();

    public ConnectionHistory(string folder)
    {
        _file = Path.Combine(folder, "connection-history.json");
        Load();
    }

    /// <summary>
    /// The recorded peer IDs, most-recent first. Each entry is a raw digit string
    /// (e.g. <c>"418205793"</c>) — callers use <see cref="FlashDeskId.Format"/> for display.
    /// </summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>
    /// Records a successful connection. The entry is moved to the front if already present,
    /// and the list is trimmed to <see cref="MaxEntries"/> after insertion.
    /// </summary>
    public void Add(string digits)
    {
        _entries.Remove(digits);         // dedup: remove wherever it currently sits
        _entries.Insert(0, digits);      // most-recent first
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);
        Save();
    }

    // ── persistence ──────────────────────────────────────────────────────────

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            var saved = JsonSerializer.Deserialize<Data>(File.ReadAllText(_file));
            if (saved?.Entries is null) return;
            foreach (var e in saved.Entries)
                if (!string.IsNullOrWhiteSpace(e)) _entries.Add(e);
        }
        catch { _entries.Clear(); }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(new Data([.. _entries])));
            File.Move(tmp, _file, overwrite: true);
        }
        catch { /* history is cosmetic — never worth interrupting a session */ }
    }

    private sealed record Data(List<string> Entries);
}

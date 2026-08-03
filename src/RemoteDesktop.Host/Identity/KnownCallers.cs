using System.Text.Json;

namespace RemoteDesktop.Host.Identity;

/// <summary>
/// Every FlashDesk number that has successfully connected to THIS machine before.
///
/// Its only job is to let the consent dialog say, in words, whether a caller is new or returning
/// — the single most valuable thing in that dialog, because the tech-support scam always begins
/// with a number the person has never seen. A first-time caller also gets the short delay before
/// Accept becomes clickable.
///
/// It lives beside the identity in the user's own profile and is NEVER sent anywhere: not to the
/// relay, not to the other side, not to Conor. It is a private note this machine keeps for itself.
/// </summary>
public sealed class KnownCallers
{
    private readonly string _file;
    private readonly object _gate = new();
    private Dictionary<string, Entry> _entries = new();

    public sealed record Entry(DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc, int Times);

    public KnownCallers(string folder)
    {
        _file = Path.Combine(folder, "known-callers.json");
        Load();
    }

    /// <summary>Has this number been accepted on this machine before?</summary>
    public bool IsKnown(string id)
    {
        lock (_gate) return _entries.ContainsKey(id);
    }

    public Entry? Get(string id)
    {
        lock (_gate) return _entries.TryGetValue(id, out var e) ? e : null;
    }

    /// <summary>Records a caller the person actually accepted. Rejected callers are NOT recorded.</summary>
    public void Remember(string id)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _entries[id] = _entries.TryGetValue(id, out var existing)
                ? existing with { LastSeenUtc = now, Times = existing.Times + 1 }
                : new Entry(now, now, 1);
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            _entries = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_file)) ?? new();
        }
        catch
        {
            _entries = new(); // an unreadable list must never stop a session; treat everyone as new
        }
    }

    // Called under _gate. Written then moved, so a crash cannot leave a truncated list.
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            string tmp = _file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _file, overwrite: true);
        }
        catch { /* failing to remember is not worth interrupting a session for */ }
    }
}

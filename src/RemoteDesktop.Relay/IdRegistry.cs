using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RemoteDesktop.Shared.Identity;

namespace RemoteDesktop.Relay;

/// <summary>
/// The relay's record of which FlashDesk ID belongs to which installation.
///
/// It stores a SALTED HASH of each secret, never the secret itself, so a leak of this file
/// exposes nothing usable. The secret is 256 bits of randomness, so a single salted SHA-256 is
/// enough here — the slow-hash machinery that human passwords need buys nothing against a value
/// nobody can guess.
///
/// Persisted to disk because the binding must survive a relay restart: if it did not, a restart
/// would let anyone re-claim any ID.
/// </summary>
public sealed class IdRegistry
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();

    public sealed record Entry(string Salt, string Hash, DateTimeOffset FirstSeenUtc, DateTimeOffset LastSeenUtc);

    public IdRegistry(string path)
    {
        _path = path;
        Load();
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    /// <summary>
    /// Claim an ID. Unknown ID -> bound to this secret. Known ID with the matching secret -> the
    /// same installation coming back (a restart), accepted. Known ID with a different secret ->
    /// refused, and the caller is expected to generate a new ID and retry.
    /// </summary>
    public RegistrationResponse Register(string id, string secret)
    {
        if (!FlashDeskId.IsValid(id) || string.IsNullOrWhiteSpace(secret) || secret.Length > 512)
            return RegistrationResponse.Refused(RegistrationOutcome.Malformed);

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_entries.TryGetValue(id, out var existing))
            {
                if (!SecretMatches(secret, existing))
                    return RegistrationResponse.Refused(RegistrationOutcome.IdTakenByAnother);

                _entries[id] = existing with { LastSeenUtc = now };
                Save();
                return RegistrationResponse.Ok();
            }

            string salt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            _entries[id] = new Entry(salt, HashSecret(secret, salt), now, now);
            Save();
            return RegistrationResponse.Ok();
        }
    }

    /// <summary>Is this ID currently known to the relay? Used by /health and diagnostics only.</summary>
    public bool IsKnown(string id)
    {
        lock (_gate) return _entries.ContainsKey(id);
    }

    private static bool SecretMatches(string secret, Entry entry)
    {
        // Fixed-time comparison: a normal string compare leaks how much of the hash matched.
        var candidate = Encoding.UTF8.GetBytes(HashSecret(secret, entry.Salt));
        var stored = Encoding.UTF8.GetBytes(entry.Hash);
        return CryptographicOperations.FixedTimeEquals(candidate, stored);
    }

    private static string HashSecret(string secret, string salt) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(salt + secret)));

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var loaded = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path));
            if (loaded is null) return;
            foreach (var kv in loaded) _entries[kv.Key] = kv.Value;
        }
        catch (Exception ex)
        {
            // A corrupt registry must not stop the relay from serving; start empty and say so.
            Console.Error.WriteLine($"registry load failed ({ex.Message}); starting empty");
        }
    }

    // Always called under _gate. Written to a temporary file and moved into place, so a crash
    // mid-write cannot leave a truncated registry behind.
    private void Save()
    {
        try
        {
            string tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"registry save failed: {ex.Message}");
        }
    }
}

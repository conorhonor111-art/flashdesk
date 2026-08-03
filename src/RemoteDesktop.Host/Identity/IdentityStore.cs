using System.Security.Cryptography;
using System.Text.Json;
using RemoteDesktop.Shared.Identity;

// This project has its own RemoteDesktop.Host.Encoding namespace (the tile encoder), which
// shadows System.Text.Encoding inside it. Alias it rather than rename either one.
using Utf8 = System.Text.Encoding;

namespace RemoteDesktop.Host.Identity;

/// <summary>
/// Where this installation's FlashDesk ID and secret live on the client's machine.
///
/// Location: <c>%APPDATA%\FlashDesk\identity.json</c> — inside the user's own profile, so writing
/// it needs NO administrator rights, and it never sits next to the executable. Consequences,
/// recorded in CLAUDE.md and true by construction here:
///   SURVIVES  — program updates, deleting and re-downloading the exe, emptying Downloads.
///   DOES NOT  — deleting this folder, a wiped Windows profile, or a different Windows account
///               on the same machine (each account gets its own ID).
///
/// The secret is protected with DPAPI (CurrentUser), so another account on the same machine
/// cannot read it even with the file in hand. It is never displayed, never read aloud, never
/// shown in the UI.
/// </summary>
public sealed class IdentityStore
{
    private readonly string _folder;
    private readonly string _file;

    public IdentityStore()
    {
        // FLASHDESK_CONFIG_DIR exists so two copies can run side by side with separate numbers
        // on one machine — that is how the relay pairing is tested without a second computer.
        // Note it cannot be replaced by setting APPDATA: Windows resolves the Application Data
        // folder through the shell, not that environment variable (found out the hard way).
        string? overrideDir = Environment.GetEnvironmentVariable("FLASHDESK_CONFIG_DIR");
        _folder = string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlashDesk")
            : overrideDir;
        _file = Path.Combine(_folder, "identity.json");
    }

    public string FilePath => _file;

    /// <summary>Where this installation keeps everything it remembers, on this machine only.</summary>
    public string Folder => _folder;

    private sealed record Stored(string Id, string SecretProtected);

    /// <summary>The stored identity, or null if this is a first run (or the file is unreadable).</summary>
    public (string Id, string Secret)? Load()
    {
        try
        {
            if (!File.Exists(_file)) return null;
            var stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(_file));
            if (stored is null || !FlashDeskId.IsValid(stored.Id)) return null;

            byte[] plain = ProtectedData.Unprotect(
                Convert.FromBase64String(stored.SecretProtected), null, DataProtectionScope.CurrentUser);
            return (stored.Id, Utf8.UTF8.GetString(plain));
        }
        catch
        {
            // Unreadable or written by a different Windows account: treat as a first run rather
            // than crashing. A new ID will be generated and the client is told plainly.
            return null;
        }
    }

    /// <summary>Writes the pair, creating the folder. Throws if the disk refuses — the caller reports it.</summary>
    public void Save(string id, string secret)
    {
        Directory.CreateDirectory(_folder);
        byte[] protectedSecret = ProtectedData.Protect(
            Utf8.UTF8.GetBytes(secret), null, DataProtectionScope.CurrentUser);

        string json = JsonSerializer.Serialize(
            new Stored(id, Convert.ToBase64String(protectedSecret)),
            new JsonSerializerOptions { WriteIndented = true });

        // Write then move, so a crash mid-write cannot leave a half-written identity behind.
        string tmp = _file + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _file, overwrite: true);
    }

    /// <summary>Creates a brand-new ID + secret and stores them.</summary>
    public (string Id, string Secret) CreateNew()
    {
        string id = FlashDeskId.Generate();
        string secret = FlashDeskId.GenerateSecret();
        Save(id, secret);
        return (id, secret);
    }
}

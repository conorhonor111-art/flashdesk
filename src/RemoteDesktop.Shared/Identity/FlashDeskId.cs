using System.Security.Cryptography;

namespace RemoteDesktop.Shared.Identity;

/// <summary>
/// The FlashDesk ID: a permanent 9-digit address for one installation, shown as three groups of
/// three (418 205 793) because its only job is to be read aloud down a phone by a flustered
/// non-technical person.
///
/// THE PRINCIPLE (CLAUDE.md): the ID is an ADDRESS, not a credential. A billion combinations
/// sounds like a lot and is not — an internet-facing service will be scanned — so knowing an ID
/// must never, on its own, get anyone anywhere. The secret below proves ownership; the
/// allow-list and the consent dialog are what protect the machine.
///
/// Deliberately NEVER derived from MAC address, hostname or any hardware value: derived IDs leak
/// hardware information and are predictable.
/// </summary>
public static class FlashDeskId
{
    public const int Digits = 9;

    /// <summary>Bytes of randomness in the secret. 32 bytes = 256 bits, far beyond guessing.</summary>
    public const int SecretBytes = 32;

    /// <summary>
    /// A fresh random ID from a cryptographic random number generator. The first digit is 1-9:
    /// a leading zero gets dropped when a person reads the number aloud or types it back.
    /// </summary>
    public static string Generate()
    {
        Span<char> digits = stackalloc char[Digits];
        digits[0] = (char)('1' + RandomNumberGenerator.GetInt32(0, 9));
        for (int i = 1; i < Digits; i++)
            digits[i] = (char)('0' + RandomNumberGenerator.GetInt32(0, 10));
        return new string(digits);
    }

    /// <summary>A fresh random secret, base64 encoded for storage and transport.</summary>
    public static string GenerateSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes));

    /// <summary>True if the value is exactly 9 digits and does not start with zero.</summary>
    public static bool IsValid(string? id)
    {
        if (id is null || id.Length != Digits) return false;
        if (id[0] < '1' || id[0] > '9') return false;
        for (int i = 1; i < Digits; i++)
            if (id[i] < '0' || id[i] > '9') return false;
        return true;
    }

    /// <summary>"418205793" -> "418 205 793". Grouping is what makes it readable aloud.</summary>
    public static string Format(string id) =>
        IsValid(id) ? $"{id[..3]} {id[3..6]} {id[6..]}" : id;

    /// <summary>Strips spaces and any other separators a person might type or paste.</summary>
    public static string Normalise(string? typed)
    {
        if (string.IsNullOrEmpty(typed)) return string.Empty;
        Span<char> buffer = stackalloc char[typed.Length];
        int n = 0;
        foreach (char c in typed)
            if (c >= '0' && c <= '9') buffer[n++] = c;
        return new string(buffer[..n]);
    }
}

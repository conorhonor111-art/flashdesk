using System.Text;

namespace RemoteDesktop.Shared.Protocol;

/// <summary>
/// Says whether the host can currently see its own screen, and gives the operator a sentence to
/// read when it cannot.
///
/// Layout: [1 byte: 1 = available, 0 = not][UTF-8 words, no length prefix — the message framing
/// already knows how long the payload is].
///
/// The words travel on the wire rather than being chosen by the viewer, because only the host knows
/// WHY it cannot see its screen, and a wrong guess here is worse than no guess: telling someone
/// their helper's computer is locked when it is actually busy sends them to the wrong place.
/// </summary>
public readonly record struct ScreenStatePayload(bool Available, string Words)
{
    /// <summary>Kept small on purpose — this is one sentence for a person, not a log line.</summary>
    public const int MaxWordBytes = 512;

    public byte[] ToBytes()
    {
        var words = Encoding.UTF8.GetBytes(Words ?? string.Empty);
        if (words.Length > MaxWordBytes) words = words[..MaxWordBytes];

        var bytes = new byte[1 + words.Length];
        bytes[0] = Available ? (byte)1 : (byte)0;
        words.CopyTo(bytes, 1);
        return bytes;
    }

    public static ScreenStatePayload FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 1) throw new ArgumentException("Screen-state message is too short.", nameof(bytes));

        bool available = bytes[0] != 0;
        string words = bytes.Length > 1 ? Encoding.UTF8.GetString(bytes[1..]) : string.Empty;
        return new ScreenStatePayload(available, words);
    }

    /// <summary>
    /// The sentence the operator sees. Plain words about the other person's computer, naming the
    /// ordinary causes, so nobody concludes the program has crashed.
    /// </summary>
    public static ScreenStatePayload Unavailable() => new(false,
        "Their screen is not available right now — this happens when the computer is locked, "
        + "showing a Windows security prompt, or switching users. The connection is still open.");

    public static ScreenStatePayload Available_() => new(true, "Their screen is back.");
}

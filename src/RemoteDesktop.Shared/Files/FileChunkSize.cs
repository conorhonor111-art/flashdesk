using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Shared.Files;

/// <summary>
/// Decides how big one piece of a file should be, from what the link has actually been measured to
/// carry. Pure arithmetic — no sockets, no disk — so the rule can be read and argued with.
///
/// <para><b>WHY A FIXED CHUNK IS WRONG, and it is not about memory.</b> Every send on a connection
/// is serialised behind one lock. While a chunk is being written, the screen frames and the latency
/// ping wait behind it. The viewer measures that wait as round-trip time; the bandwidth governor
/// reads a round trip well above the session's own best as a link backing up, and answers by
/// stepping the picture down. So on a slow uplink a fixed 256 KB chunk does not merely slow the
/// picture — it FREEZES it and then blurs it, because a chunk takes roughly 430 ms to leave a
/// 0.6 MB/s link and the governor concludes the link is congested. The transfer would be
/// sabotaging the screen it is not competing with.</para>
///
/// <para><b>So the chunk is sized from the link, to a time budget, not to a byte count.</b>
/// <see cref="ProtocolConstants.MaxChunkSendHoldMs"/> is the target hold, and it sits deliberately
/// under the governor's queue threshold.</para>
///
/// <para><b>THE MEASURED RATE READS HIGH, AND THAT IS ACCOUNTED FOR HERE.</b>
/// <c>BandwidthGovernor.EstimatedBytesPerSecond</c> times a send that blocked, and the first part
/// of every send vanishes into the socket buffer before the timing starts — CLAUDE.md records it
/// measured 2–3× high against a known 600 KB/s link, and the governor's own status line labels it
/// "reads high". Taking it at face value would produce chunks two to three times too big, which is
/// precisely the failure this class exists to prevent. So the estimate is divided by the worst end
/// of that measured overstatement before anything is computed from it. Being wrong towards small
/// costs a little overhead; being wrong towards large costs the picture.</para>
///
/// <para><b>Honest limit.</b> On a genuinely terrible link the floor wins and the hold goes over
/// budget anyway — 16 KB down a 30 KB/s uplink is half a second whatever this class says. Nothing
/// can fix that; a link that slow cannot carry a file and a picture at the same time.</para>
/// </summary>
public static class FileChunkSize
{
    /// <summary>
    /// How much the measured burst rate overstates the link, at the worst end of what was measured.
    /// See the class comment: this is a recorded measurement, not a safety fudge.
    /// </summary>
    public const double RateOverstatement = 3.0;

    /// <summary>
    /// The chunk to send next, given what the link has been measured to carry in bytes per second.
    /// Pass 0 (or anything not a real positive number) when nothing has been measured yet.
    /// </summary>
    public static int ForMeasuredRate(double measuredBytesPerSecond)
    {
        // Nothing measured, or a nonsense reading. Not the ceiling — see UnmeasuredFileChunkBytes.
        if (double.IsNaN(measuredBytesPerSecond) || measuredBytesPerSecond <= 0)
            return ProtocolConstants.UnmeasuredFileChunkBytes;

        if (double.IsInfinity(measuredBytesPerSecond))
            return ProtocolConstants.MaxFileChunkBytes;

        double believable = measuredBytesPerSecond / RateOverstatement;
        double budget = believable * (ProtocolConstants.MaxChunkSendHoldMs / 1000.0);

        // Clamped in double before the cast: a rate near long.MaxValue would otherwise overflow the
        // int and come out negative, which would be a chunk size of nonsense from a number that was
        // merely large.
        double clamped = Math.Clamp(budget, ProtocolConstants.MinFileChunkBytes, ProtocolConstants.MaxFileChunkBytes);
        return (int)clamped;
    }

    /// <summary>
    /// How long a chunk of this size would take to leave a link of this speed, in milliseconds.
    /// Exists so the technical view can state the real hold rather than implying it, and so the
    /// tests can assert the budget is met rather than assert the arithmetic against itself.
    /// </summary>
    public static double HoldMsAt(int chunkBytes, double trueBytesPerSecond) =>
        trueBytesPerSecond <= 0 ? double.PositiveInfinity : chunkBytes * 1000.0 / trueBytesPerSecond;
}

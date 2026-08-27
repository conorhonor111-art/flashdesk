using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Net;

/// <summary>
/// Decides, second by second, how many frames to send and at what JPEG quality, so a session keeps
/// working on a real home connection instead of stalling. Pure arithmetic — no sockets, no screen,
/// no UI — so the rule it follows can be read, argued with, and exercised by the link test without
/// a network.
///
/// WHY THIS EXISTS (measured, not assumed). The fixed quality-95 default was chosen for a LAN, where
/// the extra bytes are free. Full motion costs ~1.7-2.2 MB/s; a typical home upload is 0.6-2.5 MB/s.
/// So on a real pair of home connections a still screen would have been fine and anything moving
/// would have stalled.
///
/// THE ONE LADDER. Frame rate and quality are NOT two independent controls — they are one ordered
/// list of operating points, walked in one direction at a time. That is deliberate:
///
///   - It removes the "which do I sacrifice first?" question, which has no stable answer, and with
///     two independent controllers is where oscillation comes from.
///   - The order inside it is set by MEASUREMENT, not taste. CLAUDE.md records that quality 95 costs
///     only ~7% more bytes than quality 70 on REAL screen content (real desktops have large flat
///     areas that compress well at any quality). So dropping quality 95->60 saves roughly a tenth of
///     the bytes, while dropping 30->8 fps saves nearly three quarters. Frame rate is the lever that
///     actually moves the number; quality is a trim applied only after frame rate is spent.
///   - It also happens to protect the thing this tool exists for: text legibility is the primary
///     quality metric (Stage 1), and this ladder keeps quality at 95 through the first five steps.
///
///   level  fps  quality   what it is
///     0     30    95      a LAN or a fast link — identical to the old fixed behaviour
///     1     20    95
///     2     12    95
///     3      8    95
///     4      5    95      choppy motion, text still perfectly sharp
///     5      5    85
///     6      5    75
///     7      5    65
///     8      3    65
///     9      2    60      the floor: a slideshow, still readable
///
/// Level 0 IS today's fixed behaviour, so nothing that was measured on the LAN regresses.
///
/// DOWN FAST, UP SLOW. A stall is felt immediately and must be escaped immediately; a quality
/// recovery that arrives a second late is not noticed at all. So overload steps down at once (two
/// levels when a single send has already blocked for most of a second), while recovery needs
/// several consecutive quiet windows. The two thresholds are far apart on purpose — the gap between
/// them is the dead zone that stops the picture visibly breathing between two levels.
/// </summary>
public sealed class BandwidthGovernor
{
    /// <summary>One operating point on the ladder: a frame-rate cap and a quality, taken together.</summary>
    public readonly record struct Step(int Fps, int Quality);

    private static readonly Step[] Ladder =
    {
        new(30, 95),
        new(20, 95),
        new(12, 95),
        new( 8, 95),
        new( 5, 95),
        new( 5, 85),
        new( 5, 75),
        new( 5, 65),
        new( 3, 65),
        new( 2, ProtocolConstants.MinJpegQuality),
    };

    /// <summary>
    /// THE SIGNAL: time spent blocked in send, divided by the time the frames were supposed to take.
    /// Above 1 means a frame cannot be pushed out within its own interval, so the picture is falling
    /// behind real time — which is exactly what a person means by "it stalled".
    ///
    /// It is deliberately NOT the fraction of wall-clock time spent sending. That was tried first and
    /// measured wrong: a healthy link running near capacity legitimately spends most of its time
    /// sending, so the rule kept backing off a link that had nothing wrong with it (measured: 22 fps
    /// where 29 was available). Being busy is not the same as being late.
    /// </summary>
    /// It is set BELOW 1 on purpose. At 1 the rule would fill the link completely, and a completely
    /// full link is a slow one: every frame then waits behind the bytes still draining from the last,
    /// so the picture is permanently a frame or more behind and the operator's clicks appear late.
    /// This is a remote-CONTROL tool, so latency beats throughput — the settling point deliberately
    /// leaves roughly a third of the link unused, and buys back that delay with it.
    private const double HeavyOverrun = 0.85;

    /// <summary>...and below this there is room to give something back. The gap is the dead zone.</summary>
    private const double LightOverrun = 0.40;

    /// <summary>One send blocking this long is already a visible stall — react without waiting.</summary>
    private const double PanicSendMs = 700;

    /// <summary>How far past the interval's byte budget a frame may go before it counts as too big.</summary>
    private const double OverBudgetMargin = 1.5;

    /// <summary>How often the rule is applied. Per-frame decisions would chase noise.</summary>
    private const int DecisionWindowMs = 500;

    /// <summary>Consecutive quiet windows before giving a level back (~1.5 s).</summary>
    private const int QuietWindowsToRecover = 3;

    /// <summary>A window this cheap means the screen is essentially still — recover faster.</summary>
    private const int NearlySilentBytes = 20 * 1024;

    /// <summary>Below these a send returned before the socket buffer filled, so it timed nothing.</summary>
    private const double MeasurableSendMs = 15;
    private const int MeasurableBytes = 8 * 1024;

    /// <summary>
    /// Round-trip delay ABOVE this session's own best, at which the link is judged backed up. It is
    /// measured against the session's baseline, never as an absolute: a genuinely distant machine has
    /// a high round trip with nothing wrong, and must not be throttled for being far away.
    /// </summary>
    /// <remarks>⚠ <c>ProtocolConstants.MaxChunkSendHoldMs</c> is pinned UNDER this number: a file
    /// chunk holds the same send lock the ping waits on, so a chunk allowed to hold it longer than
    /// this would make every transfer look to the governor like a link backing up, and the picture
    /// would drop for no reason. If this moves, that must move with it.</remarks>
    private const double QueueHeavyMs = 250;
    private const double QueueSevereMs = 750;

    /// <summary>Below this the pipe is judged clear, and the level may be given back.</summary>
    private const double QueueClearMs = 90;

    /// <summary>Idle step-down. Nothing has changed, so there is nothing to send a frame rate FOR.</summary>
    private const int IdleAfterMs = 1000;
    private const int DeepIdleAfterMs = 4000;
    private const int IdleFps = 8;
    private const int DeepIdleFps = 2;

    /// <summary>Longest a capture may block. Bounds how late a session notices it was cancelled.</summary>
    private const int MaxCaptureTimeoutMs = 500;

    private int _level;
    private long _windowStartedAt;
    private double _windowSendMs;
    private double _windowIntervalMs;
    private long _windowBytes;
    private double _windowWorstSendMs;
    private int _quietWindows;
    private long _lastActivityAt;
    private double _rateEstimate;    // bytes/s, EWMA; 0 = never measured
    private double _baselineRttMs;   // the best round trip this session has seen: the empty-pipe figure
    private double _queueDelayMs;    // how far the latest round trip sits above that baseline

    // Published for the technical view. Written by the frame loop, read by the UI thread; simple
    // types, and a stale read is worth nothing more than a half-second-old readout.
    private volatile int _publishedFps = Ladder[0].Fps;
    private volatile int _publishedQuality = Ladder[0].Quality;

    public BandwidthGovernor() => Reset();

    /// <summary>
    /// Pins the quality, for the operator's own testing from the technical view. Null = automatic.
    /// Frame rate keeps adapting either way: a pinned quality must not be able to stall a session.
    /// </summary>
    public int? ManualQuality { get; set; }

    /// <summary>Quality the encoder should use for the next tiles.</summary>
    public int Quality => ManualQuality ?? _publishedQuality;

    /// <summary>Frames per second to aim for right now — the ladder, capped further when idle.</summary>
    public int TargetFps => _publishedFps;

    public int FrameIntervalMs => Math.Max(1, 1000 / Math.Max(1, TargetFps));

    /// <summary>
    /// How long the capture may block waiting for the screen to change. On the DXGI path this is a
    /// real wait that returns the instant something moves, so a long idle timeout costs nothing and
    /// delays nothing; on GDI it is a poll interval, which is exactly where the idle step-down saves
    /// the client's CPU (GDI copies the whole screen every frame whether it changed or not).
    /// </summary>
    public int CaptureTimeoutMs => Math.Clamp(FrameIntervalMs, 5, MaxCaptureTimeoutMs);

    /// <summary>Measured link rate in bytes/s, or 0 while nothing has been slow enough to time.</summary>
    public double EstimatedBytesPerSecond => _rateEstimate;

    /// <summary>
    /// How big the next piece of a file should be, from what this link has been measured to carry.
    /// The arithmetic and its reasoning live in <see cref="Shared.Files.FileChunkSize"/>; it is
    /// surfaced here so the one caller does not have to know where the rate comes from.
    /// </summary>
    public int FileChunkBytes => Shared.Files.FileChunkSize.ForMeasuredRate(_rateEstimate);

    /// <summary>
    /// A file chunk just went out. Feeds the RATE ESTIMATE ONLY — it deliberately does not touch
    /// the ladder.
    ///
    /// <para>Why it must feed the estimate: the rate is otherwise learned only from screen frames,
    /// and a frame only measures anything when it blocked. During a download of a still screen the
    /// frames are tiny and never block, so without this the link would stay "not yet measured" for
    /// the whole transfer and every chunk would be the cautious unmeasured default. The chunks are
    /// the biggest thing on the wire at that moment; they are the best measurement available.</para>
    ///
    /// <para>Why it must NOT move the ladder: a transfer legitimately fills the link, and being
    /// full is not the same as being broken. If it genuinely delays the picture, the round-trip
    /// signal sees that and steps down on evidence rather than on the mere fact that a file is
    /// moving.</para>
    /// </summary>
    public void OnBulkSent(int bytes, double sendMs)
    {
        if (sendMs < MeasurableSendMs || bytes < MeasurableBytes) return;

        double sample = bytes / (sendMs / 1000.0);
        _rateEstimate = _rateEstimate <= 0 ? sample : (_rateEstimate * 0.7) + (sample * 0.3);
    }

    /// <summary>
    /// Below this, a link is already known to be nowhere near what a transfer could contend for —
    /// stepping down for one would degrade a session that never needed it. Well above ordinary
    /// full-motion cost (~2 MB/s, CLAUDE.md), so this only ever holds on genuinely fast links.
    /// </summary>
    private const double KnownFastBytesPerSecond = 3 * 1024 * 1024;

    /// <summary>
    /// A file transfer just started sharing the channel (see HostFileService's transfer-start
    /// callback, wired via HostServer). Steps down AT ONCE when the link is not already known to be
    /// fast, rather than waiting for <see cref="OnRoundTripReported"/> to notice.
    ///
    /// <para><b>Why the round trip cannot be trusted to react in time here.</b> The viewer's Ping
    /// carries its LAST measured round trip — one full ping-pong cycle old — and the Host's own Pong
    /// reply is queued behind the very file chunks it would be reporting on, through the same send
    /// lock. Measured consequence (PROGRESS.md, 2026-08-27): a real transfer left quality unchanged
    /// for its entire length and only stepped down once the transfer — and the reason for stepping
    /// down — was already over. This call exists to react to the cause directly instead of to a
    /// signal that structurally cannot arrive before the cause has passed.</para>
    ///
    /// <para>Skipped on a known-fast link, so this never touches a session that does not need it —
    /// the same evidence-first principle <see cref="OnBulkSent"/> already applies to the rate
    /// estimate, just checked a moment earlier than the round trip can manage.</para>
    /// </summary>
    public void OnBulkTransferStarted()
    {
        if (_rateEstimate > 0 && _rateEstimate >= KnownFastBytesPerSecond) return;
        StepDown(2);
        OpenWindow(Environment.TickCount64);
    }

    /// <summary>
    /// The transfer above just ended. Does NOT step the ladder up — recovery still has to earn it
    /// through <see cref="OnFrameSent"/>'s ordinary quiet-window climb, same "up slow" principle as
    /// everywhere else here; a step down that needed no evidence does not buy a step up that needs
    /// none either.
    ///
    /// <para>What this DOES do: clears the queue-delay reading, because the one sitting here right
    /// now may still be the stale, transfer-inflated kind <see cref="OnBulkTransferStarted"/>'s own
    /// doc comment describes — measured while the Pong carrying it was queued behind the chunks that
    /// just stopped. Left alone, that stale reading would hold recovery hostage for one more round
    /// trip after the real cause is already gone, which is the exact shape of defect this pair of
    /// methods exists to fix. Clearing it means the next real round trip — due within about one Ping
    /// interval — is judged on its own evidence. <see cref="_quietWindows"/> is reset alongside it so
    /// a window straddling the transfer's end cannot half-count.</para>
    /// </summary>
    public void OnBulkTransferEnded()
    {
        _queueDelayMs = 0;
        _quietWindows = 0;
    }

    public int Level => _level;
    public int LadderSize => Ladder.Length;

    /// <summary>Starts a session at the top of the ladder — a new link is assumed good until proven slow.</summary>
    public void Reset()
    {
        _level = 0;
        _quietWindows = 0;
        _rateEstimate = 0;
        _baselineRttMs = 0;
        _queueDelayMs = 0;
        _windowSendMs = 0;
        _windowIntervalMs = 0;
        _windowBytes = 0;
        _windowWorstSendMs = 0;
        _windowStartedAt = Environment.TickCount64;
        _lastActivityAt = Environment.TickCount64;
        Publish();
    }

    /// <summary>
    /// The viewer just sent input, so the person is working right now. Counts as activity in its own
    /// right, before any pixel has changed: on the GDI path this is what removes the wake-up delay,
    /// because the hand always moves before the screen does.
    /// </summary>
    public void OnInputReceived() => _lastActivityAt = Environment.TickCount64;

    /// <summary>
    /// The viewer reported the round trip it just measured (carried in its next Ping — see
    /// PingPayload). This is the ONLY signal that can see a queue building inside a home router.
    ///
    /// Why it is needed on top of send timing: while a deep buffer is filling, every send still
    /// returns instantly, so the sending machine has no local evidence of trouble at all. Measured on
    /// a simulated 0.6 MB/s link with 1 MB of buffering, send timing alone left the picture 1.7 s
    /// behind and never recovered, because the sender only offered ~5% more than the link could
    /// carry and took half a minute to fill the buffer. A round trip sees that queue on the first
    /// probe after it forms.
    ///
    /// Compared against this session's own best round trip, never an absolute number: a machine two
    /// thousand kilometres away starts at 60 ms with nothing wrong with it.
    /// </summary>
    public void OnRoundTripReported(double roundTripMs)
    {
        if (roundTripMs < 0) return; // the viewer has not measured one yet

        if (_baselineRttMs <= 0 || roundTripMs < _baselineRttMs)
            _baselineRttMs = roundTripMs;
        else
            // Creep upward very slowly, so a path that genuinely got longer (a phone moving onto a
            // different network) eventually re-baselines instead of being throttled forever. Slow
            // enough that a standing queue can never drag the baseline up with it.
            _baselineRttMs += (roundTripMs - _baselineRttMs) * 0.01;

        _queueDelayMs = roundTripMs - _baselineRttMs;

        if (_queueDelayMs >= QueueSevereMs) StepDown(2);
        else if (_queueDelayMs >= QueueHeavyMs) StepDown(1);
        Publish();
    }

    /// <summary>
    /// Report one sent frame. <paramref name="sendMs"/> is wall-clock time blocked inside the send —
    /// the honest congestion signal, because once the socket's own buffer is full a send cannot
    /// complete faster than the link drains.
    /// </summary>
    public void OnFrameSent(int bytes, double sendMs, bool screenChanged, bool cursorMoved)
    {
        long now = Environment.TickCount64;
        if (screenChanged || cursorMoved) _lastActivityAt = now;

        _windowSendMs += sendMs;
        _windowIntervalMs += FrameIntervalMs; // what this frame was budgeted, at the rate then in force
        _windowBytes += bytes;
        if (sendMs > _windowWorstSendMs) _windowWorstSendMs = sendMs;

        // Only a send that actually blocked measured anything. A small frame disappears into the
        // socket buffer and returns instantly, which says nothing about the link.
        if (sendMs >= MeasurableSendMs && bytes >= MeasurableBytes)
        {
            double sample = bytes / (sendMs / 1000.0);
            _rateEstimate = _rateEstimate <= 0 ? sample : (_rateEstimate * 0.7) + (sample * 0.3);
        }

        // A single send that blocked most of a second is already a stall on screen. Do not wait for
        // the window to close before escaping it.
        if (sendMs >= PanicSendMs)
        {
            StepDown(2);
            OpenWindow(now);
            return;
        }

        // Safety net for a deep buffer. A home router can hold a megabyte, and while it is filling,
        // sends still return instantly — so the blocking signal above says everything is fine while
        // the picture slides further and further behind. Once the link's rate is known at all, a
        // frame far larger than that rate can carry within its own interval is over budget whether
        // it blocked or not. The margin is generous because the rate estimate reads high (the first
        // chunk of every send disappears into the buffer before the timing starts).
        if (_rateEstimate > 0 && _level < Ladder.Length - 1)
        {
            double budget = _rateEstimate * (FrameIntervalMs / 1000.0);
            if (bytes > budget * OverBudgetMargin)
            {
                StepDown(1);
                OpenWindow(now);
                return;
            }
        }

        long elapsed = now - _windowStartedAt;
        if (elapsed < DecisionWindowMs) { Publish(); return; }

        double overrun = _windowSendMs / Math.Max(1.0, _windowIntervalMs);
        if (overrun >= HeavyOverrun)
        {
            StepDown(1);
        }
        else if (overrun <= LightOverrun && _queueDelayMs < QueueClearMs)
        {
            // Both signals must agree before anything is given back. Send timing alone would happily
            // climb again while a router queue was still draining, and put it straight back.
            _quietWindows++;
            bool nearlySilent = _windowBytes < NearlySilentBytes && _windowWorstSendMs < MeasurableSendMs;
            if (_quietWindows >= QuietWindowsToRecover)
            {
                // A window with almost no traffic in it means the screen went still, so the level we
                // fell to was for content that is no longer on screen. Climb faster, or the picture
                // would stay soft for ten seconds after a video stops.
                StepUp(nearlySilent ? 2 : 1);
                _quietWindows = 0;
            }
        }
        else
        {
            _quietWindows = 0; // in the dead zone: hold this level, which is the point of having one
        }

        OpenWindow(now);
    }

    private void OpenWindow(long now)
    {
        _windowStartedAt = now;
        _windowSendMs = 0;
        _windowIntervalMs = 0;
        _windowBytes = 0;
        _windowWorstSendMs = 0;
        Publish();
    }

    private void StepDown(int levels)
    {
        _level = Math.Min(_level + levels, Ladder.Length - 1);
        _quietWindows = 0;
    }

    private void StepUp(int levels) => _level = Math.Max(_level - levels, 0);

    // The effective frame rate is the ladder's, capped again when nothing is happening. Recovery from
    // idle is instant and deliberately not stepped: the first thing a person does after a still
    // moment is move something, and that must not arrive late.
    private void Publish()
    {
        var level = Ladder[_level];
        long still = Environment.TickCount64 - _lastActivityAt;
        int fps = level.Fps;
        if (still >= DeepIdleAfterMs) fps = Math.Min(fps, DeepIdleFps);
        else if (still >= IdleAfterMs) fps = Math.Min(fps, IdleFps);

        _publishedFps = fps;
        _publishedQuality = level.Quality;
    }

    /// <summary>One line for the technical view. Never shown in the simple view — see CLAUDE.md.</summary>
    public string StateLine()
    {
        var level = Ladder[_level];
        // Labelled "burst" and "reads high" on purpose: this figure is NOT the link's bandwidth and
        // must not be quoted as one. It times a send that blocked, and the first part of every send
        // vanishes into the socket buffer before the timing starts, so it overstates — measured 2-3x
        // high against a known 600 KB/s link. It is fit only for the coarse over-budget backstop.
        string burst = _rateEstimate > 0 ? $"{_rateEstimate / 1024.0:0} KB/s (reads high)" : "not yet measured";
        string queue = _baselineRttMs > 0 ? $"{_queueDelayMs:0} ms over {_baselineRttMs:0} ms" : "no round trip yet";
        return $"Adaptive: level {_level}/{Ladder.Length - 1} - {level.Fps} fps cap, quality {level.Quality}"
             + $"{(ManualQuality is null ? "" : $" (quality pinned to {ManualQuality})")}"
             + $"; send burst {burst}; queue {queue}";
    }
}

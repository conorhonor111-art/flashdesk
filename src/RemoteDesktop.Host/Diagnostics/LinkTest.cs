using System.Diagnostics;
using System.Text;
using RemoteDesktop.Host.Encoding;
using RemoteDesktop.Host.Net;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// Measures what a session actually feels like on a SLOW link, with and without adaptation, and
/// writes the two side by side.
///
/// WHY IT IS SIMULATED RATHER THAN THROTTLED FOR REAL. The dev machine is reached over a remote
/// desktop connection, so throttling its real network would risk cutting the operator off from the
/// machine running the test. A leaky bucket reproduces the part that matters exactly — once the
/// operating system's send buffer is full, a send cannot complete faster than the link drains — and
/// it is repeatable to the byte, which a real home connection never is.
///
/// WHAT IS SIMULATED: two things only. The screen (a generated pattern, so the same content is used
/// on every machine and every run) and the link (the bucket). EVERYTHING else is the shipping code:
/// the real <see cref="JpegTileEncoder"/>, the real <see cref="TileDiffer"/>, the real
/// <see cref="FramePacket"/> framing through the real <see cref="MessageChannel"/>, and above all
/// the real <see cref="BandwidthGovernor"/> — the policy under test is not reimplemented here.
///
/// THE NUMBER THAT MATTERS is not fps and not KB/s. It is HOW OLD THE PICTURE IS on the other
/// person's screen: the gap between the moment something happened on the shared screen and the
/// moment the last byte describing it has left. That is what "the motion stalls" means when a person
/// says it, and it is what adaptation exists to bound.
/// </summary>
public static class LinkTest
{
#if DEBUG
    private const string BuildConfig = "Debug";
#else
    private const string BuildConfig = "Release";
#endif

    private const int Width = 1920;
    private const int Height = 1080;

    /// <summary>The shared screen animates at this rate regardless of what we manage to send. Frames
    /// we do not keep up with are simply never sent — which is the behaviour being demonstrated.</summary>
    private const int ScreenAnimationFps = 30;

    /// <summary>Round trip on an empty pipe — roughly Kyiv to the Warsaw relay and back.</summary>
    private const double BaselineRoundTripMs = 30;

    /// <summary>
    /// One row of the comparison. <paramref name="InFlightKb"/> is how many bytes may sit in flight
    /// before a send blocks — the sender's socket buffer, the relay's buffers, the receiver's window
    /// and, above all, the home router's own queue. It is tested at two values because it is the
    /// parameter that decides how badly fixed-rate sending fails, and because on a real consumer
    /// connection nobody knows it: 256 KB is a tight, well-behaved path, while 1 MB is an ordinary
    /// home router with a deep buffer. A deeper buffer does not slow the link — it just lets a sender
    /// that refuses to slow down bury the picture further behind, which is the whole problem.
    /// </summary>
    private readonly record struct Scenario(int LinkKbPerSecond, int InFlightKb, bool FullMotion);

    private static readonly Scenario[] Scenarios =
    {
        new( 600,  256, true),
        new( 600,  256, false),
        new( 600, 1024, true),
        new( 600, 1024, false),
        new(2500,  256, true),
        new(2500,  256, false),
    };

    public static string Run(string? outputPath = null, int secondsPerRun = 8)
    {
        var report = new StringBuilder();

        // The self-test goes FIRST, and at the top rather than in an appendix, because a failure
        // here is the kind that must not be scrolled past: it says the program's own housekeeping
        // is broken on this machine, which matters more than any number below it.
        var selfTest = SelfTest.Run();
        report.AppendLine(selfTest.Report);
        report.AppendLine();

        report.AppendLine("FlashDesk link test — adaptive quality and frame rate");
        report.AppendLine("====================================================");
        report.AppendLine($"Time            : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Machine         : {Environment.MachineName}");
        report.AppendLine($"Logical CPUs    : {Environment.ProcessorCount}");
        report.AppendLine($"Build config    : {BuildConfig}");
        report.AppendLine($"Screen          : {Width} x {Height}, tile {ProtocolConstants.TileSize}px");
        report.AppendLine($"Run length      : {secondsPerRun}s each");
        report.AppendLine();
        report.AppendLine("FIXED    = the old behaviour: quality 95, 30 fps, no adaptation.");
        report.AppendLine("ADAPTIVE = the governor chooses the frame rate and quality from the measured link.");
        report.AppendLine("full motion = every pixel changes every frame (the worst case a link can be given).");
        report.AppendLine("typical     = a still screen with one moving region, like a support session.");
        report.AppendLine("in flight   = bytes allowed to sit in the pipe before a send blocks: socket buffers,");
        report.AppendLine("              the relay, and the home router's own queue. 1 MB is an ordinary router.");
        report.AppendLine();
        report.AppendLine("PICTURE BEHIND BY = how old the image on the other person's screen is, averaged over");
        report.AppendLine("the whole run and at its worst. This is the number that decides whether a session feels");
        report.AppendLine("alive or broken, and it is felt as input lag: click, then wait that long to see it.");
        report.AppendLine();
        report.AppendLine("Link      In flight  Content      Mode      fps    KB/s    behind avg  behind worst  SETTLED AT  ends at");
        report.AppendLine("--------  ---------  -----------  --------  -----  ------  ----------  ------------  ----------  ----------------------");

        foreach (var scenario in Scenarios)
        {
            foreach (bool adaptive in new[] { false, true })
            {
                var r = RunOne(scenario, adaptive, secondsPerRun).GetAwaiter().GetResult();
                report.AppendLine(
                    $"{scenario.LinkKbPerSecond / 1024.0,6:0.0} MB  {scenario.InFlightKb,6} KB  " +
                    $"{(scenario.FullMotion ? "full motion" : "typical"),-11}  " +
                    $"{(adaptive ? "ADAPTIVE" : "FIXED"),-8}  {r.Fps,5:0.0}  {r.KbPerSec,6:0}  " +
                    $"{r.AvgBehindMs / 1000.0,9:0.00}s  {r.WorstBehindMs / 1000.0,11:0.00}s  " +
                    $"{r.LateBehindMs / 1000.0,9:0.00}s  {r.Ending}");
            }
            report.AppendLine();
        }

        report.AppendLine("How to read this:");
        report.AppendLine("- SETTLED AT is the most important column: the age over the last quarter of the run,");
        report.AppendLine("  once everything has found its level. A long session feels like this number, not like");
        report.AppendLine("  the average, which still has the first bad seconds mixed into it.");
        report.AppendLine("- Compare the two rows of each pair. Same link, same content, only adaptation differs.");
        report.AppendLine("- 'behind worst' on a FIXED row is the stall: the picture frozen that many seconds back.");
        report.AppendLine("- Look hardest at the 1 MB in-flight rows. That is an ordinary home router, and it is");
        report.AppendLine("  where refusing to slow down costs the most — the bytes are not lost, they are queued,");
        report.AppendLine("  and a queue is exactly the input lag we are trying to avoid.");
        report.AppendLine("- 'dropped' counts screen changes never sent. Dropping is CORRECT — the alternative is a");
        report.AppendLine("  queue, and a queue turns into input lag that never catches up.");
        report.AppendLine("- On the fast link both modes should look similar: adaptation must cost nothing when");
        report.AppendLine("  there is nothing to fix. If ADAPTIVE is worse there, the ladder is starting too low.");
        report.AppendLine("- ADAPTIVE usually sends FEWER KB/s than FIXED. That is the point, not a defect: the");
        report.AppendLine("  unused part of the link is what keeps the picture close to live.");

        outputPath ??= Path.Combine(DesktopOrBase(),
            $"FlashDesk-linktest-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        // With a byte-order mark, so Notepad and PowerShell both read the punctuation correctly
        // rather than showing mojibake. (System.Text is spelled out because this project has its own
        // RemoteDesktop.Host.Encoding namespace, which shadows it.)
        File.WriteAllText(outputPath, report.ToString(), new System.Text.UTF8Encoding(true));
        return outputPath;
    }

    private readonly record struct RunResult(
        double Fps, double KbPerSec, double AvgBehindMs, double WorstBehindMs, double LateBehindMs,
        int Dropped, string Ending);

    /// <summary>
    /// One run. This mirrors HostServer's frame loop deliberately closely — capture, diff, encode,
    /// send, one frame at a time with nothing buffered — but the POLICY it is testing is not copied:
    /// it calls the same BandwidthGovernor the host does.
    /// </summary>
    private static async Task<RunResult> RunOne(Scenario scenario, bool adaptive, int seconds)
    {
        bool fullMotion = scenario.FullMotion;
        var clock = Stopwatch.StartNew();
        var link = new ThrottledStream(scenario.LinkKbPerSecond * 1024, scenario.InFlightKb * 1024, clock);
        using var channel = new MessageChannel(link);

        var differ = new TileDiffer(ProtocolConstants.TileSize);
        differ.Configure(Width, Height);
        var governor = new BandwidthGovernor();
        int fixedQuality = ProtocolConstants.DefaultJpegQuality;
        var encoder = new JpegTileEncoder(fixedQuality);
        var pixels = new byte[Width * Height * 4];
        var backdrop = new byte[Width * Height * 4];
        FillTypicalBackdrop(backdrop);

        long frameNumber = 0;
        int lastAnimationFrame = -1;
        int dropped = 0;
        double lastPingAtMs = 0;
        var sendTimer = new Stopwatch();

        // Age of the picture, weighted by how long it is the picture. A frame arriving at time a
        // carrying content from time c is what the viewer looks at until the NEXT frame arrives, and
        // it gets older the whole time it is on screen. Measuring only the age at the instant it
        // arrives (which is what the first version of this test did) misses the gap between frames
        // entirely, and the gap is most of what a person feels when they say the picture stalled.
        double weightedAge = 0, weightedSpan = 0, worstAge = 0;
        double lateAge = 0, lateSpan = 0;
        double lateStartMs = seconds * 1000.0 * 0.75;
        double previousArrival = 0, previousContent = 0;
        bool havePrevious = false;

        void AccumulateAge(double content, double from, double until)
        {
            double span = until - from;
            if (span <= 0) return;
            weightedAge += (((from + until) / 2.0) - content) * span;
            weightedSpan += span;
            double oldest = until - content;
            if (oldest > worstAge) worstAge = oldest;

            // The same figure over the last quarter of the run only. The average over a whole run
            // mixes the recovery in with the trouble it recovered from, so a mode that climbs out of
            // a full router buffer and one that never does can average out to nearly the same
            // number. This column is the one that says which of the two happened.
            double lateFrom = Math.Max(from, lateStartMs);
            if (until <= lateFrom) return;
            lateAge += (((lateFrom + until) / 2.0) - content) * (until - lateFrom);
            lateSpan += until - lateFrom;
        }

        while (clock.ElapsedMilliseconds < seconds * 1000L)
        {
            long loopStart = clock.ElapsedMilliseconds;

            int quality = adaptive ? governor.Quality : fixedQuality;
            int intervalMs = adaptive ? governor.FrameIntervalMs : 1000 / ScreenAnimationFps;
            encoder.SetQuality(quality);

            // The shared screen moves on its own clock. Whatever we did not keep up with was dropped,
            // exactly as the real loop drops it: the next capture returns the LATEST screen, not a
            // backlog of the ones in between.
            int animationFrame = (int)(loopStart * ScreenAnimationFps / 1000);
            if (lastAnimationFrame >= 0 && animationFrame > lastAnimationFrame + 1)
                dropped += animationFrame - lastAnimationFrame - 1;
            lastAnimationFrame = animationFrame;

            if (fullMotion) FillFullMotion(pixels, animationFrame);
            else FillTypical(pixels, backdrop, animationFrame);
            double contentAtMs = clock.Elapsed.TotalMilliseconds;

            var updates = new List<TileUpdate>();
            var changed = differ.Diff(pixels, Width, Height, quality);
            foreach (var t in changed)
                updates.Add(new TileUpdate(t.Column, t.Row, encoder.Encode(pixels, Width, t.X, t.Y, t.Width, t.Height)));

            if (adaptive && changed.Count < 8)
            {
                var refresh = new List<TileDiffer.ChangedTile>();
                differ.CollectStale(quality, 6, Width, Height, refresh);
                foreach (var t in refresh)
                    updates.Add(new TileUpdate(t.Column, t.Row, encoder.Encode(pixels, Width, t.X, t.Y, t.Width, t.Height)));
            }

            var bytes = new FramePacket(frameNumber++, updates).ToBytes();

            sendTimer.Restart();
            await channel.SendAsync(MessageType.Frame, bytes).ConfigureAwait(false);
            sendTimer.Stop();

            double arrivalMs = link.DrainCompleteAtMs; // when this frame's last byte has left
            if (havePrevious) AccumulateAge(previousContent, previousArrival, arrivalMs);
            previousArrival = arrivalMs;
            previousContent = contentAtMs;
            havePrevious = true;

            if (adaptive)
            {
                governor.OnFrameSent(bytes.Length, sendTimer.Elapsed.TotalMilliseconds, changed.Count > 0, cursorMoved: false);

                // The viewer reports a round trip once a second. Simulated the way it really behaves:
                // the reply has to follow everything already queued ahead of it, so the round trip is
                // the idle path delay plus however long the pipe takes to clear.
                double nowMs = clock.Elapsed.TotalMilliseconds;
                if (nowMs - lastPingAtMs >= 1000)
                {
                    lastPingAtMs = nowMs;
                    governor.OnRoundTripReported(BaselineRoundTripMs + (link.DrainCompleteAtMs - nowMs));
                }
            }

            int remaining = (adaptive ? governor.FrameIntervalMs : intervalMs) - (int)(clock.ElapsedMilliseconds - loopStart);
            if (remaining > 0) await Task.Delay(remaining).ConfigureAwait(false);
        }

        // The last frame stays on screen until the run ends; it keeps ageing while it is there.
        if (havePrevious) AccumulateAge(previousContent, previousArrival, clock.Elapsed.TotalMilliseconds);

        double elapsedSeconds = clock.Elapsed.TotalSeconds;
        string ending = adaptive
            ? governor.StateLine()
            : $"fixed {ScreenAnimationFps} fps, quality {fixedQuality}";

        return new RunResult(
            frameNumber / elapsedSeconds,
            link.TotalBytes / 1024.0 / elapsedSeconds,
            weightedSpan > 0 ? weightedAge / weightedSpan : 0,
            worstAge,
            lateSpan > 0 ? lateAge / lateSpan : 0,
            dropped,
            ending);
    }

    /// <summary>
    /// A link that drains at a fixed rate with a fixed amount allowed in flight — a leaky bucket.
    /// Small writes vanish into the bucket and return at once; a big one blocks until there is room,
    /// which is precisely how a real socket behaves on a saturated uplink.
    /// </summary>
    private sealed class ThrottledStream : Stream
    {
        private readonly double _bytesPerMs;
        private readonly int _capacity;
        private readonly Stopwatch _clock;
        private double _queued;
        private double _lastDrainMs;

        public long TotalBytes { get; private set; }

        public ThrottledStream(int bytesPerSecond, int capacity, Stopwatch clock)
        {
            _bytesPerMs = bytesPerSecond / 1000.0;
            _capacity = capacity;
            _clock = clock;
            _lastDrainMs = clock.Elapsed.TotalMilliseconds;
        }

        private void Drain()
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            _queued = Math.Max(0, _queued - ((now - _lastDrainMs) * _bytesPerMs));
            _lastDrainMs = now;
        }

        /// <summary>Clock time at which everything written so far will have finished leaving.</summary>
        public double DrainCompleteAtMs
        {
            get
            {
                Drain();
                return _clock.Elapsed.TotalMilliseconds + (_queued / _bytesPerMs);
            }
        }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            int remaining = buffer.Length;
            while (remaining > 0)
            {
                Drain();
                double room = _capacity - _queued;
                if (room >= 1)
                {
                    int take = (int)Math.Min(remaining, room);
                    _queued += take;
                    remaining -= take;
                    TotalBytes += take;
                    continue;
                }

                // Wait exactly long enough for room to appear, rather than polling.
                double waitMs = Math.Min(remaining, _capacity) / _bytesPerMs;
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(waitMs, 1, 250)), ct).ConfigureAwait(false);
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => false;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    // Every pixel changes every frame. Same formula as the diagnostics PATTERN so the two reports
    // stay comparable; kept separate so tuning one can never silently move the other's baseline.
    private static void FillFullMotion(byte[] bgra, int frame)
    {
        int i = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bgra[i++] = (byte)(x + frame * 3);
                bgra[i++] = (byte)(y + frame * 2);
                bgra[i++] = (byte)(((x >> 3) + (y >> 3) + frame) * 8);
                bgra[i++] = 255;
            }
        }
    }

    // A still screen with flat areas and edges, like a window full of text.
    private static void FillTypicalBackdrop(byte[] bgra)
    {
        int i = 0;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool ink = (y % 24) < 2 && (x % 11) < 7 && x > 200 && x < 1500;
                byte v = ink ? (byte)32 : (byte)244;
                bgra[i++] = v;
                bgra[i++] = v;
                bgra[i++] = v;
                bgra[i++] = 255;
            }
        }
    }

    // The same still screen with one region moving — a video, a scrolling list, a progress animation.
    private static void FillTypical(byte[] bgra, byte[] backdrop, int frame)
    {
        Array.Copy(backdrop, bgra, backdrop.Length);

        const int boxW = 480, boxH = 270;
        int originX = 120 + (int)(Math.Sin(frame / 12.0) * 100);
        int originY = 400;
        for (int y = 0; y < boxH; y++)
        {
            int row = (originY + y) * Width * 4;
            for (int x = 0; x < boxW; x++)
            {
                int o = row + ((originX + x) * 4);
                bgra[o] = (byte)(x + frame * 5);
                bgra[o + 1] = (byte)(y + frame * 3);
                bgra[o + 2] = (byte)(((x >> 2) + (y >> 2) + frame) * 6);
            }
        }
    }

    private static string DesktopOrBase()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir;
    }
}

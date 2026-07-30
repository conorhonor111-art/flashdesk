using System.Diagnostics;
using System.Text;
using RemoteDesktop.Host.Capture;
using RemoteDesktop.Host.Encoding;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Diagnostics;

/// <summary>
/// Runs a fixed, repeatable benchmark and writes the numbers to a text file, so measurement does not
/// depend on a person reading a moving window. Three conditions, each at JPEG quality 70/85/95:
///
///   IDLE       — real capture against the untouched screen, paced at 30 fps; what a still session costs.
///   PATTERN    — a program-generated full-motion 1920x1080 image (identical on every machine and run),
///                encoded as fast as possible; isolates encoder throughput and byte cost.
///   END-TO-END — the REAL capture path plus real encode, against a real moving screen, run flat out.
///                This is the only phase that measures capture + encode TOGETHER — the number that
///                actually decides whether a machine (especially one on the GDI fallback) is usable.
/// </summary>
public static class DiagnosticRunner
{
#if DEBUG
    private const string BuildConfig = "Debug";
#else
    private const string BuildConfig = "Release";
#endif

    private static readonly int[] Qualities = { 70, 85, 95 };
    private const int PatternWidth = 1920;
    private const int PatternHeight = 1080;

    public static string Run(string? outputPath = null, int idleSeconds = 10, int patternFrames = 150, bool forceGdi = false)
    {
        var report = new StringBuilder();
        using var capture = ScreenCaptureFactory.Create(out var dxgiReason, forceGdi);
        string method = capture.Method == CaptureMethod.Dxgi ? "DXGI Desktop Duplication" : "GDI BitBlt (fallback)";

        report.AppendLine("FlashDesk diagnostics");
        report.AppendLine("=====================");
        report.AppendLine($"Time            : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        report.AppendLine($"Machine         : {Environment.MachineName}");
        report.AppendLine($"OS              : {Environment.OSVersion.VersionString}");
        report.AppendLine($"Logical CPUs    : {Environment.ProcessorCount}");
        report.AppendLine($"Build config    : {BuildConfig}");
        report.AppendLine($"Capture method  : {method}");
        if (forceGdi)
            report.AppendLine("Capture forced  : GDI (for the DXGI-vs-GDI comparison)");
        else if (capture.Method == CaptureMethod.Gdi && !string.IsNullOrEmpty(dxgiReason))
            report.AppendLine($"                  (DXGI unavailable: {dxgiReason})");
        report.AppendLine($"Captured screen : {capture.Width} x {capture.Height}");
        report.AppendLine($"Pattern size    : {PatternWidth} x {PatternHeight} (identical on every machine)");
        report.AppendLine($"Settings        : tile {ProtocolConstants.TileSize}px, idle {idleSeconds}s, pattern {patternFrames} frames");
        report.AppendLine();
        report.AppendLine("IDLE       = untouched screen, paced at 30 fps, real capture path.");
        report.AppendLine("PATTERN    = program-drawn full-motion frames, encoded as fast as possible (no capture).");
        report.AppendLine("END-TO-END = real capture + real encode against a real moving screen, flat out. THE usability number.");
        report.AppendLine();
        report.AppendLine("Quality  Condition    fps      KB/s       encode ms/frame  capture ms/frame  tiles/frame");
        report.AppendLine("-------  ----------  ------  ----------  ---------------  ----------------  -----------");

        // END-TO-END for all qualities is measured in one continuous motion session (one flash).
        var endToEnd = MeasureEndToEndAll(capture, 5);

        double maxIdleTiles = 0;
        foreach (int quality in Qualities)
        {
            var idle = MeasureIdle(capture, quality, idleSeconds);
            maxIdleTiles = Math.Max(maxIdleTiles, idle.TilesPerFrame);
            report.AppendLine(Row(quality, "IDLE", idle));
            report.AppendLine(Row(quality, "PATTERN", MeasurePattern(quality, patternFrames)));
            report.AppendLine(Row(quality, "END-TO-END", endToEnd[quality]));
        }

        report.AppendLine();
        report.AppendLine("How to read this:");
        report.AppendLine("- IDLE KB/s is what a still session costs; it should be small at every quality.");
        report.AppendLine("- PATTERN measures the encoder alone on identical content — compare it between two machines.");
        report.AppendLine("- END-TO-END fps is real capture+encode together. On the GDI fallback this is the true ceiling;");
        report.AppendLine("  if it is well under 30, that machine will feel slow no matter what else is right.");

        if (maxIdleTiles > 0.5)
        {
            report.AppendLine();
            report.AppendLine($"** IDLE CONTAMINATED: {maxIdleTiles:0.0} tiles/frame changed during the idle measurement.");
            report.AppendLine("   Something on screen was updating (a visible window, the taskbar clock, a notification).");
            report.AppendLine("   A clean idle is ~0 tiles/frame — minimise other windows and rerun for a trustworthy idle figure.");
        }

        outputPath ??= Path.Combine(
            DesktopOrBase(),
            $"FlashDesk-diagnostics-{Environment.MachineName}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(outputPath, report.ToString());
        return outputPath;
    }

    private readonly record struct Result(double Fps, double KbPerSec, double EncodeMs, double CaptureMs, double TilesPerFrame, bool HasCapture);

    private static string Row(int quality, string condition, Result r)
    {
        string capture = r.HasCapture ? $"{r.CaptureMs,16:0.00}" : $"{"-",16}";
        return $"{quality,-7}  {condition,-10}  {r.Fps,6:0.0}  {r.KbPerSec,10:0.0}  {r.EncodeMs,15:0.00}  {capture}  {r.TilesPerFrame,11:0.0}";
    }

    private static Result MeasureIdle(IScreenCapture capture, int quality, int seconds)
    {
        var differ = new TileDiffer(ProtocolConstants.TileSize);
        differ.Configure(capture.Width, capture.Height);
        var encoder = new JpegTileEncoder(quality);

        if (capture.TryCapture(50, out var warm)) differ.Diff(warm.Pixels, warm.Width, warm.Height);

        long frames = 0, totalBytes = 0, tiles = 0;
        double captureMs = 0, encodeMs = 0;
        int intervalMs = 1000 / 30;
        var sw = Stopwatch.StartNew();
        var timer = new Stopwatch();

        while (sw.ElapsedMilliseconds < seconds * 1000L)
        {
            long frameStart = sw.ElapsedMilliseconds;
            int frameBytes = 8 + 4 + 4 + 1 + 4;
            int frameTiles = 0;

            timer.Restart();
            bool got = capture.TryCapture(intervalMs, out var frame);
            captureMs += timer.Elapsed.TotalMilliseconds;

            if (got)
            {
                foreach (var t in differ.Diff(frame.Pixels, frame.Width, frame.Height))
                {
                    timer.Restart();
                    var jpeg = encoder.Encode(frame.Pixels, frame.Width, t.X, t.Y, t.Width, t.Height);
                    encodeMs += timer.Elapsed.TotalMilliseconds;
                    frameBytes += 12 + jpeg.Length;
                    frameTiles++;
                }
            }

            frames++;
            totalBytes += frameBytes;
            tiles += frameTiles;

            int remaining = intervalMs - (int)(sw.ElapsedMilliseconds - frameStart);
            if (remaining > 0) Thread.Sleep(remaining);
        }
        sw.Stop();

        double s = sw.Elapsed.TotalSeconds;
        return new Result(frames / s, totalBytes / 1024.0 / s,
            frames > 0 ? encodeMs / frames : 0, frames > 0 ? captureMs / frames : 0,
            frames > 0 ? (double)tiles / frames : 0, HasCapture: true);
    }

    private static Result MeasurePattern(int quality, int frameCount)
    {
        var differ = new TileDiffer(ProtocolConstants.TileSize);
        differ.Configure(PatternWidth, PatternHeight);
        var encoder = new JpegTileEncoder(quality);
        var buffer = new byte[PatternWidth * PatternHeight * 4];

        FillPattern(buffer, 0);
        differ.Diff(buffer, PatternWidth, PatternHeight);

        long totalBytes = 0, tiles = 0;
        double encodeMs = 0;
        var timer = new Stopwatch();
        var sw = Stopwatch.StartNew();

        for (int f = 1; f <= frameCount; f++)
        {
            FillPattern(buffer, f);
            int frameBytes = 8 + 4 + 4 + 1 + 4;
            int frameTiles = 0;
            foreach (var t in differ.Diff(buffer, PatternWidth, PatternHeight))
            {
                timer.Restart();
                var jpeg = encoder.Encode(buffer, PatternWidth, t.X, t.Y, t.Width, t.Height);
                encodeMs += timer.Elapsed.TotalMilliseconds;
                frameBytes += 12 + jpeg.Length;
                frameTiles++;
            }
            totalBytes += frameBytes;
            tiles += frameTiles;
        }
        sw.Stop();

        double s = sw.Elapsed.TotalSeconds;
        return new Result(frameCount / s, totalBytes / 1024.0 / s,
            encodeMs / frameCount, 0, (double)tiles / frameCount, HasCapture: false);
    }

    // Real capture + real encode against a real moving screen, flat out, for each quality — measured
    // in one continuous motion session so the screen only flashes once.
    private static Dictionary<int, Result> MeasureEndToEndAll(IScreenCapture capture, int secondsEach)
    {
        var results = new Dictionary<int, Result>();
        var motion = new MotionWindow();
        motion.Start();
        try
        {
            foreach (int quality in Qualities)
                results[quality] = MeasureEndToEnd(capture, quality, secondsEach);
        }
        finally
        {
            motion.Stop();
        }
        return results;
    }

    private static Result MeasureEndToEnd(IScreenCapture capture, int quality, int seconds)
    {
        var differ = new TileDiffer(ProtocolConstants.TileSize);
        differ.Configure(capture.Width, capture.Height);
        var encoder = new JpegTileEncoder(quality);

        if (capture.TryCapture(200, out var warm)) differ.Diff(warm.Pixels, warm.Width, warm.Height);

        long frames = 0, totalBytes = 0, tiles = 0;
        double captureMs = 0, encodeMs = 0;
        var sw = Stopwatch.StartNew();
        var timer = new Stopwatch();

        while (sw.ElapsedMilliseconds < seconds * 1000L)
        {
            int frameBytes = 8 + 4 + 4 + 1 + 4;
            int frameTiles = 0;

            timer.Restart();
            bool got = capture.TryCapture(100, out var frame);
            captureMs += timer.Elapsed.TotalMilliseconds;

            if (got)
            {
                foreach (var t in differ.Diff(frame.Pixels, frame.Width, frame.Height))
                {
                    timer.Restart();
                    var jpeg = encoder.Encode(frame.Pixels, frame.Width, t.X, t.Y, t.Width, t.Height);
                    encodeMs += timer.Elapsed.TotalMilliseconds;
                    frameBytes += 12 + jpeg.Length;
                    frameTiles++;
                }
            }

            frames++;
            totalBytes += frameBytes;
            tiles += frameTiles;
        }
        sw.Stop();

        double s = sw.Elapsed.TotalSeconds;
        return new Result(frames / s, totalBytes / 1024.0 / s,
            frames > 0 ? encodeMs / frames : 0, frames > 0 ? captureMs / frames : 0,
            frames > 0 ? (double)tiles / frames : 0, HasCapture: true);
    }

    // Deterministic moving pattern: colour gradients plus a moving blocky field so every tile changes
    // every frame and the encoder has real, repeatable work. Identical on every machine and run.
    private static void FillPattern(byte[] bgra, int frame)
    {
        int i = 0;
        for (int y = 0; y < PatternHeight; y++)
        {
            for (int x = 0; x < PatternWidth; x++)
            {
                bgra[i++] = (byte)(x + frame * 3);                     // B
                bgra[i++] = (byte)(y + frame * 2);                     // G
                bgra[i++] = (byte)(((x >> 3) + (y >> 3) + frame) * 8); // R (blocky, moving)
                bgra[i++] = 255;                                       // A
            }
        }
    }

    private static string DesktopOrBase()
    {
        string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        return string.IsNullOrEmpty(dir) ? AppContext.BaseDirectory : dir;
    }
}

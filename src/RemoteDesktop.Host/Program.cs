using RemoteDesktop.Host.Diagnostics;

namespace RemoteDesktop.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Headless benchmark mode: `RemoteDesktop.Host.exe --diagnostics [outputPath] [idleSeconds]`.
        // Runs the fixed diagnostic sequence, writes the report, and exits without showing a window.
        if (args.Length > 0 && args[0] == "--diagnostics")
        {
            string? outPath = args.Length > 1 ? args[1] : null;
            int idle = args.Length > 2 && int.TryParse(args[2], out var s) ? s : 10;
            bool forceGdi = args.Contains("gdi");
            string written = DiagnosticRunner.Run(outPath, idle, forceGdi: forceGdi);
            Console.WriteLine(written);
            return;
        }

        // Headless link test: `FlashDesk.exe --linktest [outputPath] [secondsPerRun]`. Runs the real
        // encoder and the real bandwidth governor against a simulated slow link and writes the
        // before/after comparison. Needs no network and no screen, so it is safe to run over a
        // remote desktop connection — throttling the machine's real network is not.
        if (args.Length > 0 && args[0] == "--linktest")
        {
            string? outPath = args.Length > 1 ? args[1] : null;
            int perRun = args.Length > 2 && int.TryParse(args[2], out var secs) ? secs : 8;
            Console.WriteLine(LinkTest.Run(outPath, perRun));
            return;
        }

        // The housekeeping checks on their own: `FlashDesk.exe --selftest`. Prints the report and
        // exits 0 for PASS, 1 for FAIL, so it can be checked without anyone reading it. The same
        // checks run from the technical view's button and at the top of --linktest — one piece of
        // code, three ways in, so they can never disagree about what passing means.
        if (args.Length > 0 && args[0] == "--selftest")
        {
            var result = SelfTest.Run();
            Console.WriteLine(result.Report);
            Environment.Exit(result.Passed ? 0 : 1);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

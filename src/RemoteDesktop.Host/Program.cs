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

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

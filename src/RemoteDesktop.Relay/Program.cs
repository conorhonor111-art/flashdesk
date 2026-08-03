using System.Diagnostics;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Relay;

/// <summary>
/// The FlashDesk relay — Stage 3 skeleton.
///
/// What exists today: the service runs under systemd, restarts on crash and on reboot, and
/// answers /health so a human can confirm from a browser that the relay is alive. The WebSocket
/// pairing logic (FlashDesk ID registration, heartbeat, byte-piping between host and viewer)
/// lands in the next steps.
///
/// It listens on 127.0.0.1:5000 ONLY. Caddy terminates TLS on 443 and forwards here, so the
/// relay is never directly exposed to the internet and never handles certificates itself.
/// </summary>
public static class Program
{
    /// <summary>Bumped by hand when something a human would want to distinguish changes.</summary>
    private const string Version = "0.1.0-skeleton";

    private static readonly DateTimeOffset StartedUtc = DateTimeOffset.UtcNow;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:5000");

        var app = builder.Build();

        // Deliberately plain text, not JSON: this page exists so a non-technical person can open
        // it in a browser after any change and see in one glance whether the relay is alive.
        app.MapGet("/health", () =>
        {
            var uptime = DateTimeOffset.UtcNow - StartedUtc;
            string body =
                $"""
                FlashDesk relay
                status   : OK
                version  : {Version}
                protocol : {ProtocolConstants.ProtocolVersion}
                started  : {StartedUtc:yyyy-MM-dd HH:mm:ss} UTC
                uptime   : {(int)uptime.TotalDays}d {uptime.Hours:00}h {uptime.Minutes:00}m {uptime.Seconds:00}s
                """;
            return Results.Text(body + Environment.NewLine, "text/plain; charset=utf-8");
        });

        app.Run();
    }
}

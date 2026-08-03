using RemoteDesktop.Shared.Identity;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Relay;

/// <summary>
/// The FlashDesk relay.
///
/// Today it registers FlashDesk IDs and answers /health. The WebSocket pairing (piping bytes
/// between a host and a viewer) lands next.
///
/// It listens on 127.0.0.1:5000 ONLY. Caddy terminates TLS on 443 and forwards here, so the
/// relay is never directly exposed to the internet and never handles certificates itself.
/// </summary>
public static class Program
{
    /// <summary>Bumped by hand when something a human would want to distinguish changes.</summary>
    private const string Version = "0.2.0-ids";

    private static readonly DateTimeOffset StartedUtc = DateTimeOffset.UtcNow;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:5000");

        // State lives outside the program directory so a redeploy never wipes the registry.
        string stateDir = Environment.GetEnvironmentVariable("FLASHDESK_STATE_DIR") ?? "/var/lib/flashdesk-relay";
        Directory.CreateDirectory(stateDir);
        var registry = new IdRegistry(Path.Combine(stateDir, "registry.json"));

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
                numbers  : {registry.Count} registered
                """;
            return Results.Text(body + Environment.NewLine, "text/plain; charset=utf-8");
        });

        // A client claims its ID here on every start. See IdRegistry for the rules.
        app.MapPost("/api/register", (RegistrationRequest request) =>
        {
            var result = registry.Register(request.Id, request.Secret);
            return result.Accepted ? Results.Ok(result) : Results.Json(result, statusCode: 409);
        });

        app.Run();
    }
}

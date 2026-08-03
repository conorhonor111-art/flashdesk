using System.Net.WebSockets;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Relay;

/// <summary>
/// The FlashDesk relay.
///
/// Both sides dial OUT to this server and it pipes bytes between them. Nothing listens for
/// incoming connections on a client machine, which is why no Windows Firewall prompt appears —
/// outbound connections need no permission.
///
/// It listens on 127.0.0.1:5000 ONLY. Caddy terminates TLS on 443 and forwards here, so the
/// relay is never directly exposed to the internet and never handles certificates itself.
///
/// It never inspects or stores session content — it copies bytes and counts them.
/// </summary>
public static class Program
{
    /// <summary>Bumped by hand when something a human would want to distinguish changes.</summary>
    private const string Version = "0.3.0-relay";

    private static readonly DateTimeOffset StartedUtc = DateTimeOffset.UtcNow;

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls("http://127.0.0.1:5000");

        // State lives outside the program directory so a redeploy never wipes the registry.
        string stateDir = Environment.GetEnvironmentVariable("FLASHDESK_STATE_DIR") ?? "/var/lib/flashdesk-relay";
        Directory.CreateDirectory(stateDir);
        var registry = new IdRegistry(Path.Combine(stateDir, "registry.json"));
        var sessions = new RelaySessions(registry);

        // Generous enough that a real person never notices, tight enough that sweeping the number
        // space is slow and shows up in the counters.
        var registerLimit = new RateLimiter(limit: 10, window: TimeSpan.FromMinutes(10));
        var connectLimit = new RateLimiter(limit: 30, window: TimeSpan.FromMinutes(10));

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

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

        // The usage page: what Conor checks to see abuse before the provider tells him about it.
        // Counts only — no numbers, no addresses, nothing about who talked to whom.
        app.MapGet("/usage", () =>
        {
            var uptime = DateTimeOffset.UtcNow - StartedUtc;
            double gb = Interlocked.Read(ref sessions.BytesPiped) / 1024.0 / 1024.0 / 1024.0;
            string body =
                $"""
                FlashDesk relay — usage since {StartedUtc:yyyy-MM-dd HH:mm} UTC ({(int)uptime.TotalDays}d {uptime.Hours:00}h)

                numbers registered in total : {registry.Count}
                computers waiting right now : {sessions.WaitingCount}
                sessions started            : {Interlocked.Read(ref sessions.SessionsStarted)}
                traffic relayed             : {gb:0.00} GB   (of 10000 GB/month allowance)

                requests turned away by the rate limit
                  registrations : {Interlocked.Read(ref registerLimit.Rejected)}
                  connections   : {Interlocked.Read(ref connectLimit.Rejected)}

                A number climbing here means someone is sweeping. Everything else being flat and
                small is normal. Counters reset when the relay restarts.
                """;
            return Results.Text(body + Environment.NewLine, "text/plain; charset=utf-8");
        });

        // A client claims its ID here on every start. See IdRegistry for the rules.
        app.MapPost("/api/register", (RegistrationRequest request, HttpContext http) =>
        {
            if (!registerLimit.Allow(CallerOf(http)))
                return Results.Text("Too many attempts. Wait a few minutes.", "text/plain", statusCode: 429);

            var result = registry.Register(request.Id, request.Secret);
            return result.Accepted ? Results.Ok(result) : Results.Json(result, statusCode: 409);
        });

        // Where sessions live. A host parks here waiting to be called; a viewer calls a number.
        app.Map("/ws", async (HttpContext http) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = 400;
                return;
            }
            if (!connectLimit.Allow(CallerOf(http)))
            {
                http.Response.StatusCode = 429;
                return;
            }

            using var socket = await http.WebSockets.AcceptWebSocketAsync();
            await sessions.HandleAsync(socket, http.RequestAborted);
        });

        // Keep the limiter's memory bounded without a background service to supervise.
        var sweeper = new Timer(_ => { registerLimit.Sweep(); connectLimit.Sweep(); },
            null, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        app.Run();
        sweeper.Dispose();
    }

    // Caddy sits in front, so the socket address is always 127.0.0.1; the real caller is in
    // X-Forwarded-For. Fall back to the socket address if the header is missing.
    private static string CallerOf(HttpContext http)
    {
        var forwarded = http.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
            return forwarded.Split(',')[0].Trim();
        return http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

using System.Net.Http.Json;
using RemoteDesktop.Shared.Identity;
using RemoteDesktop.Shared.Protocol;

namespace RemoteDesktop.Host.Identity;

/// <summary>What the window is allowed to show, and nothing else.</summary>
public enum IdentityState
{
    /// <summary>Talking to the relay. No number may be shown yet.</summary>
    Registering,

    /// <summary>The relay accepted this ID. This is the ONLY state in which a number is displayed.</summary>
    Ready,

    /// <summary>The relay could not be reached. No number — a number nobody can call is worse than an error.</summary>
    RelayUnreachable,

    /// <summary>The relay refused, repeatedly. No number.</summary>
    Refused,
}

public sealed record IdentityResult(IdentityState State, string? Id, string? Detail)
{
    /// <summary>True only when a number may appear on screen.</summary>
    public bool HasUsableId => State == IdentityState.Ready && Id is not null;
}

/// <summary>
/// Claims this installation's FlashDesk ID with the relay on startup.
///
/// The rule this class exists to enforce (CLAUDE.md): <b>the window never shows an ID the relay
/// has not accepted.</b> A valid-looking but unreachable number is worse than an error, because
/// the client reads it aloud and both ends waste ten minutes.
/// </summary>
public sealed class RelayRegistration
{
    /// <summary>Collision retries. Capped, then a clear error — never an endless loop.</summary>
    private const int MaxAttempts = 5;

    private readonly IdentityStore _store;
    private readonly HttpClient _http;

    public RelayRegistration(IdentityStore store, HttpClient? http = null)
    {
        _store = store;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public async Task<IdentityResult> RegisterAsync(CancellationToken ct = default)
    {
        var identity = _store.Load();
        bool freshlyCreated = false;

        if (identity is null)
        {
            try
            {
                identity = _store.CreateNew();
                freshlyCreated = true;
            }
            catch (Exception ex)
            {
                return new IdentityResult(IdentityState.Refused, null,
                    $"Could not save this computer's number: {ex.Message}");
            }
        }

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            RegistrationResponse? response;
            try
            {
                var reply = await _http.PostAsJsonAsync(
                    $"{ProtocolConstants.RelayBaseUrl}/api/register",
                    new RegistrationRequest(identity.Value.Id, identity.Value.Secret), ct)
                    .ConfigureAwait(false);

                response = await reply.Content.ReadFromJsonAsync<RegistrationResponse>(cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // No internet, DNS failure, relay down, or it did not answer in time.
                return new IdentityResult(IdentityState.RelayUnreachable, null, ex.Message);
            }

            if (response is null)
                return new IdentityResult(IdentityState.RelayUnreachable, null, "The relay sent an unreadable answer.");

            if (response.Accepted)
            {
                string? detail = freshlyCreated && attempt > 1
                    ? "This computer's number changed because the previous one was already in use."
                    : null;
                return new IdentityResult(IdentityState.Ready, identity.Value.Id, detail);
            }

            if (response.Outcome == RegistrationOutcome.IdTakenByAnother)
            {
                // Someone else owns this number (typically a cloned machine). Take a new one.
                try
                {
                    identity = _store.CreateNew();
                    freshlyCreated = true;
                    continue;
                }
                catch (Exception ex)
                {
                    return new IdentityResult(IdentityState.Refused, null,
                        $"Could not save a new number: {ex.Message}");
                }
            }

            return new IdentityResult(IdentityState.Refused, null,
                $"The relay refused this number ({response.Outcome}).");
        }

        return new IdentityResult(IdentityState.Refused, null,
            $"Could not get a free number after {MaxAttempts} tries.");
    }
}

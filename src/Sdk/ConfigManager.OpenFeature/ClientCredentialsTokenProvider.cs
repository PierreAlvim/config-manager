using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigManager.OpenFeature;

/// <summary>OAuth2 client-credentials token source with caching (refreshes 30s before expiry). Thread-safe.</summary>
public sealed class ClientCredentialsTokenProvider
{
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly ConfigManagerProviderOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _refreshAt;

    public ClientCredentialsTokenProvider(
        HttpClient http,
        ConfigManagerProviderOptions options,
        TimeProvider? timeProvider = null,
        ILogger<ClientCredentialsTokenProvider>? logger = null)
    {
        _http = http;
        _options = options;
        _time = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ClientCredentialsTokenProvider>.Instance;
    }

    public async ValueTask<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (TryGetCached(out var cached)) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryGetCached(out cached)) return cached;
            return await RequestTokenAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Drops the cached token (e.g. after a 401) so the next call requests a new one.</summary>
    public void Invalidate()
    {
        Volatile.Write(ref _token, null);
    }

    private bool TryGetCached(out string token)
    {
        var t = Volatile.Read(ref _token);
        if (t is not null && _time.GetUtcNow() < _refreshAt)
        {
            token = t;
            return true;
        }

        token = "";
        return false;
    }

    private async Task<string> RequestTokenAsync(CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        };
        if (!string.IsNullOrWhiteSpace(_options.Scope)) form["scope"] = _options.Scope;

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Token endpoint returned {(int)response.StatusCode}", null, response.StatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
        var root = doc.RootElement;

        var token = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Token response did not contain an access_token.");

        var expiresIn = root.TryGetProperty("expires_in", out var ei) && ei.TryGetDouble(out var seconds) ? seconds : 60;
        var lifetime = TimeSpan.FromSeconds(expiresIn) - ExpirySkew;
        _refreshAt = _time.GetUtcNow() + (lifetime > TimeSpan.Zero ? lifetime : TimeSpan.Zero);
        Volatile.Write(ref _token, token);

        _logger.LogDebug("Obtained access token for client {ClientId}, valid for {ExpiresIn}s", _options.ClientId, expiresIn);
        return token;
    }
}

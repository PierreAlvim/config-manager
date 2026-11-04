using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ConfigManager.Contracts;

namespace ConfigManager.OpenFeature;

/// <summary>Result of a conditional snapshot GET. <see cref="Snapshot"/> is null when the server answered 304.</summary>
public sealed record SnapshotFetchResult(ConfigSnapshot? Snapshot, string? ETag)
{
    public bool NotModified => Snapshot is null;
}

/// <summary>One parsed server-sent event.</summary>
public sealed record SseMessage(string Event, string Data, string? Id);

/// <summary>HTTP client for the Distribution API snapshot endpoints.</summary>
public sealed class SnapshotClient
{
    private readonly HttpClient _http;
    private readonly ClientCredentialsTokenProvider _tokens;
    private readonly ConfigManagerProviderOptions _options;
    private readonly ILogger _logger;

    public SnapshotClient(
        HttpClient http,
        ClientCredentialsTokenProvider tokens,
        ConfigManagerProviderOptions options,
        ILogger<SnapshotClient>? logger = null)
    {
        _http = http;
        _tokens = tokens;
        _options = options;
        _logger = logger ?? NullLogger<SnapshotClient>.Instance;
    }

    /// <summary>Maximum silence tolerated on the SSE stream (the server sends a keepalive every 15s).</summary>
    public TimeSpan StreamIdleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Timeout of a single (non-streaming) snapshot request.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    private string SnapshotUrl =>
        $"{_options.DistributionUrl.TrimEnd('/')}/api/v1/snapshots/{Uri.EscapeDataString(_options.Namespace)}/{Uri.EscapeDataString(_options.Environment)}";

    /// <summary>GET the snapshot; pass the previous ETag to get a 304 (<see cref="SnapshotFetchResult.NotModified"/>) when unchanged.</summary>
    public async Task<SnapshotFetchResult> GetSnapshotAsync(string? etag, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        using var request = await CreateRequestAsync(SnapshotUrl, timeout.Token).ConfigureAwait(false);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(etag) && EntityTagHeaderValue.TryParse(etag, out var parsed))
            request.Headers.IfNoneMatch.Add(parsed);

        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified)
            return new SnapshotFetchResult(null, etag);

        EnsureSuccess(response);
        var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        return new SnapshotFetchResult(SnapshotJson.Deserialize(json), response.Headers.ETag?.ToString());
    }

    /// <summary>
    /// Connects to the SSE stream and invokes <paramref name="onSnapshot"/> for every <c>snapshot</c> event until the
    /// stream ends (returns normally) or fails (throws). Cancellation via <paramref name="cancellationToken"/>.
    /// </summary>
    public async Task StreamAsync(Func<ConfigSnapshot, Task> onSnapshot, CancellationToken cancellationToken)
    {
        using var request = await CreateRequestAsync($"{SnapshotUrl}/stream", cancellationToken).ConfigureAwait(false);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await foreach (var message in SseReader.ReadAsync(stream, StreamIdleTimeout, cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(message.Event, "snapshot", StringComparison.Ordinal)) continue;

            ConfigSnapshot snapshot;
            try
            {
                snapshot = SnapshotJson.Deserialize(message.Data);
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Ignoring malformed snapshot event (id {Id})", message.Id);
                continue;
            }

            await onSnapshot(snapshot).ConfigureAwait(false);
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(string url, CancellationToken ct)
    {
        var token = await _tokens.GetTokenAsync(ct).ConfigureAwait(false);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) _tokens.Invalidate();
        throw new HttpRequestException(
            $"Distribution API returned {(int)response.StatusCode} {response.ReasonPhrase} for {response.RequestMessage?.RequestUri?.AbsolutePath}",
            null,
            response.StatusCode);
    }
}

/// <summary>Minimal Server-Sent Events parser (event / id / data fields, comments, multi-line data).</summary>
public static class SseReader
{
    public static async IAsyncEnumerable<SseMessage> ReadAsync(
        Stream stream,
        TimeSpan idleTimeout,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8);
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        string eventName = "message";
        string? id = null;
        List<string>? data = null;

        while (true)
        {
            string? line;
            try
            {
                idle.CancelAfter(idleTimeout);
                line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"No data received on the event stream for {idleTimeout}.");
            }

            if (line is null) yield break;

            if (line.Length == 0)
            {
                if (data is not null) yield return new SseMessage(eventName, string.Join("\n", data), id);
                eventName = "message";
                data = null;
                continue;
            }

            if (line[0] == ':') continue; // comment / keepalive

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? "" : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];

            switch (field)
            {
                case "event": eventName = value; break;
                case "id": id = value; break;
                case "data": (data ??= []).Add(value); break;
            }
        }
    }
}

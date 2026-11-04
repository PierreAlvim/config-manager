using System.Collections.Concurrent;
using System.Threading.Channels;
using ConfigManager.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ConfigManager.OpenFeature.Tests;

/// <summary>Tiny in-process Distribution API (token + snapshot + SSE) on a random Kestrel port.</summary>
internal sealed class FakeDistributionServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();
    private string _json = T.SnapshotJson(1).ReplaceLineEndings("");
    private long _version = 1;

    public string BaseUrl { get; private set; } = "";
    public volatile bool Down;
    public int TokenRequests;
    public int StreamConnections;
    public string? LastAuthorization;

    public FakeDistributionServer()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _app = builder.Build();

        _app.MapPost("/token", async (HttpContext ctx) =>
        {
            Interlocked.Increment(ref TokenRequests);
            var form = await ctx.Request.ReadFormAsync();
            if (form["client_id"] != "orders-service" || form["client_secret"] != "secret")
                return Results.Json(new { error = "invalid_client" }, statusCode: 401);
            return Results.Json(new { access_token = "integration-token", expires_in = 300, token_type = "Bearer" });
        });

        _app.MapGet("/api/v1/snapshots/{ns}/{env}", (HttpContext ctx) =>
        {
            LastAuthorization = ctx.Request.Headers.Authorization;
            if (Down) return Results.StatusCode(503);
            if (ctx.Request.Headers.Authorization != "Bearer integration-token") return Results.StatusCode(401);
            var etag = $"\"{_version}\"";
            ctx.Response.Headers.ETag = etag;
            if (ctx.Request.Headers.IfNoneMatch == etag) return Results.StatusCode(304);
            return Results.Content(_json, "application/json");
        });

        _app.MapGet("/api/v1/snapshots/{ns}/{env}/stream", async (HttpContext ctx) =>
        {
            if (Down) { ctx.Response.StatusCode = 503; return; }
            if (ctx.Request.Headers.Authorization != "Bearer integration-token") { ctx.Response.StatusCode = 401; return; }
            Interlocked.Increment(ref StreamConnections);
            var id = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<string>();
            _subscribers[id] = channel;
            ctx.Response.ContentType = "text/event-stream";
            try
            {
                await ctx.Response.WriteAsync($": connected\n\nevent: snapshot\nid: {_version}\ndata: {_json}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                await foreach (var message in channel.Reader.ReadAllAsync(ctx.RequestAborted))
                {
                    await ctx.Response.WriteAsync(message, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _subscribers.TryRemove(id, out _);
            }
        });
    }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        BaseUrl = _app.Urls.First().TrimEnd('/');
    }

    public ConfigManagerProviderOptions Options(Action<ConfigManagerProviderOptions>? configure = null) =>
        T.Options(o =>
        {
            o.DistributionUrl = BaseUrl;
            o.TokenEndpoint = $"{BaseUrl}/token";
            o.Mode = ConfigManagerMode.Streaming;
            configure?.Invoke(o);
        });

    /// <summary>Publishes a new snapshot version: sets it as current and pushes it to every SSE subscriber.</summary>
    public void Publish(string snapshotJson)
    {
        var compact = snapshotJson.ReplaceLineEndings("");
        var version = SnapshotJson.Deserialize(compact).Version;
        _json = compact;
        _version = version;
        foreach (var s in _subscribers.Values)
            s.Writer.TryWrite($": keepalive\n\nevent: snapshot\nid: {version}\ndata: {compact}\n\n");
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}

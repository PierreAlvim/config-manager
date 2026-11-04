using ConfigManager.Distribution.Api.Caching;
using ConfigManager.Distribution.Api.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace ConfigManager.Distribution.Api.Endpoints;

public static class SnapshotEndpoints
{
    public static IEndpointRouteBuilder MapSnapshotEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/snapshots/{ns}/{env}")
            .RequireAuthorization()
            .AddEndpointFilter<GrantFilter>();

        group.MapGet("", GetSnapshot);
        group.MapGet("stream", Stream);
        return app;
    }

    internal static IResult NoSnapshot(string ns, string env) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not Found",
            detail: $"No snapshot published for {ns}/{env}.");

    private static async Task<IResult> GetSnapshot(string ns, string env, HttpContext http, SnapshotStore store)
    {
        var snapshot = await store.GetAsync(ns, env, http.RequestAborted);
        if (snapshot is null) return NoSnapshot(ns, env);

        var etag = ETags.ForVersion(snapshot.Version);
        http.Response.Headers.ETag = etag;
        return ETags.Matches(http.Request.Headers.IfNoneMatch, etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Text(snapshot.Json, "application/json");
    }

    private static async Task<IResult> Stream(
        string ns, string env, HttpContext http, SnapshotStore store, SnapshotBroadcaster broadcaster,
        IOptions<DistributionOptions> options, CancellationToken ct)
    {
        // Subscribe first so a version published while loading is not missed.
        using var subscription = broadcaster.Subscribe(ns, env);
        var current = await store.GetAsync(ns, env, ct);
        if (current is null) return NoSnapshot(ns, env);

        var response = http.Response;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        try
        {
            var lastSent = 0L;
            await Send(current);

            while (true)
            {
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(options.Value.KeepAliveInterval);
                try
                {
                    if (!await subscription.Reader.WaitToReadAsync(wait.Token)) break;
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    await Write(SseFormatter.KeepAlive);
                    continue;
                }

                while (subscription.Reader.TryRead(out var next))
                    await Send(next);
            }

            async Task Send(StoredSnapshot s)
            {
                if (s.Version <= lastSent) return;
                lastSent = s.Version;
                await Write(SseFormatter.Snapshot(s.Version, s.Json));
            }
        }
        catch (OperationCanceledException)
        {
            // client disconnected or host shutting down
        }
        return Results.Empty;

        async Task Write(string frame)
        {
            await response.WriteAsync(frame, ct);
            await response.Body.FlushAsync(ct);
        }
    }
}

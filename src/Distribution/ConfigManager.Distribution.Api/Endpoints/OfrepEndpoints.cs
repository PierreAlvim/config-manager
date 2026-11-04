using ConfigManager.Distribution.Api.Caching;
using ConfigManager.Distribution.Api.Http;
using ConfigManager.Distribution.Api.Ofrep;

namespace ConfigManager.Distribution.Api.Endpoints;

public static class OfrepEndpoints
{
    public static IEndpointRouteBuilder MapOfrepEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/ns/{ns}/env/{env}/ofrep/v1/evaluate/flags")
            .RequireAuthorization()
            .AddEndpointFilter<GrantFilter>();

        group.MapPost("{key}", EvaluateOne);
        group.MapPost("", EvaluateAll);
        return app;
    }

    private static async Task<IResult> EvaluateOne(string ns, string env, string key, HttpContext http, SnapshotStore store)
    {
        var snapshot = await store.GetAsync(ns, env, http.RequestAborted);
        if (snapshot is null) return SnapshotEndpoints.NoSnapshot(ns, env);

        var (input, error) = await ReadContextAsync(http);
        if (error is not null)
            return Results.Json(OfrepMapper.ErrorBody(key, "PARSE_ERROR", error), statusCode: StatusCodes.Status400BadRequest);

        var result = FlagEvaluator.Evaluate(snapshot.Snapshot, key, input);
        var flagMetadata = snapshot.Snapshot.Flags.GetValueOrDefault(key)?.Metadata;
        var mapped = OfrepMapper.Map(key, result, snapshot.Version, flagMetadata);
        return Results.Json(mapped.Body, statusCode: mapped.StatusCode);
    }

    private static async Task<IResult> EvaluateAll(string ns, string env, HttpContext http, SnapshotStore store)
    {
        var snapshot = await store.GetAsync(ns, env, http.RequestAborted);
        if (snapshot is null) return SnapshotEndpoints.NoSnapshot(ns, env);

        var (input, error) = await ReadContextAsync(http);
        if (error is not null)
            return Results.Json(OfrepMapper.ErrorBody(null, "PARSE_ERROR", error), statusCode: StatusCodes.Status400BadRequest);

        var etag = OfrepMapper.BulkETag(snapshot.Version, input);
        http.Response.Headers.ETag = etag;
        if (ETags.Matches(http.Request.Headers.IfNoneMatch, etag))
            return Results.StatusCode(StatusCodes.Status304NotModified);

        var flags = snapshot.Snapshot.Flags
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => OfrepMapper.Map(f.Key, FlagEvaluator.Evaluate(f.Value, f.Key, input), snapshot.Version, f.Value.Metadata).Body)
            .ToList();

        return Results.Json(new Dictionary<string, object?>
        {
            ["flags"] = flags,
            ["metadata"] = new Dictionary<string, object?> { ["version"] = snapshot.Version },
        });
    }

    private static async Task<(EvaluationInput Input, string? Error)> ReadContextAsync(HttpContext http)
    {
        using var reader = new StreamReader(http.Request.Body);
        var body = await reader.ReadToEndAsync(http.RequestAborted);
        return ContextNormalizer.TryParseRequest(body, out var input, out var error) ? (input, null) : (input, error);
    }
}

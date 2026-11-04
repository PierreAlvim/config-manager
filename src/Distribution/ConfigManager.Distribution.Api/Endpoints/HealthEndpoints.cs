using ConfigManager.Distribution.Api.Data;

namespace ConfigManager.Distribution.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous();
        app.MapGet("/health/ready", async (ISnapshotSource source, ILogger<ISnapshotSource> logger, CancellationToken ct) =>
        {
            try
            {
                await source.PingAsync(ct);
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Readiness check failed");
                return Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).AllowAnonymous();
        return app;
    }
}

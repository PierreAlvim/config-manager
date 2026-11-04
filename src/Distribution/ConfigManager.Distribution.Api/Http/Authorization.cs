using System.Security.Claims;
using ConfigManager.Distribution.Api.Caching;
using Microsoft.AspNetCore.Diagnostics;

namespace ConfigManager.Distribution.Api.Http;

public static class ClientIds
{
    /// <summary>Client id of a service token: claim <c>azp</c>, falling back to <c>client_id</c>.</summary>
    public static string? Resolve(ClaimsPrincipal user) =>
        NonEmpty(user.FindFirstValue("azp")) ?? NonEmpty(user.FindFirstValue("client_id"));

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}

/// <summary>Requires a grant for the <c>{ns}</c>/<c>{env}</c> route values; runs after authentication.</summary>
public sealed class GrantFilter(AccessGrantStore grants) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ns = (string?)http.Request.RouteValues["ns"];
        var env = (string?)http.Request.RouteValues["env"];
        var client = ClientIds.Resolve(http.User);

        if (client is null || ns is null || env is null
            || !await grants.IsAllowedAsync(client, ns, env, http.RequestAborted))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: $"Client '{client ?? "(unknown)"}' has no access to {ns}/{env}.");
        }
        return await next(context);
    }
}

/// <summary>Maps database outages to 503 instead of a generic 500.</summary>
public sealed class DatabaseUnavailableHandler(IProblemDetailsService problems, ILogger<DatabaseUnavailableHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        if (exception is not Npgsql.NpgsqlException) return false;

        logger.LogWarning(exception, "Database unavailable while serving {Path}", httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = 503, Title = "Service Unavailable", Detail = "The configuration store is unavailable." },
        });
    }
}

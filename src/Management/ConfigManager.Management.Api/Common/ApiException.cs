using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ConfigManager.Management.Api.Common;

/// <summary>Error that maps to an RFC7807 ProblemDetails response.</summary>
public sealed class ApiException(int status, string title, string? detail = null, IReadOnlyList<string>? errors = null)
    : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IReadOnlyList<string>? Errors { get; } = errors;

    public static ApiException NotFound(string detail) => new(404, "Not found", detail);
    public static ApiException Forbidden(string detail) => new(403, "Forbidden", detail);
    public static ApiException Conflict(string detail) => new(409, "Conflict", detail);
    public static ApiException BadRequest(string detail) => new(400, "Bad request", detail);
    public static ApiException Validation(IReadOnlyList<string> errors) => new(400, "Validation failed", errors.FirstOrDefault(), errors);
}

public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var pd = exception is ApiException api
            ? new ProblemDetails { Status = api.Status, Title = api.Title, Detail = api.Detail }
            : new ProblemDetails { Status = 500, Title = "Internal server error" };

        if (exception is ApiException { Errors: { } errors })
            pd.Extensions["errors"] = errors;
        else if (exception is not ApiException)
            logger.LogError(exception, "Unhandled exception");

        context.Response.StatusCode = pd.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = pd, Exception = exception });
    }
}

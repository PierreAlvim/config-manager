using ConfigManager.Management.Api.Services;

namespace ConfigManager.Management.Api.Features.ChangeRequests;

public sealed record ReviewRequest(string? Comment);

public static class ChangeRequestEndpoints
{
    public static void MapChangeRequests(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/change-requests");

        g.MapGet("/", (string? @namespace, string? environment, string? status, ChangeRequestService svc, CancellationToken ct) =>
            svc.ListAsync(@namespace, environment, status, ct));

        g.MapGet("/{id:guid}", (Guid id, ChangeRequestService svc, CancellationToken ct) => svc.GetAsync(id, ct));

        g.MapPost("/{id:guid}/approve", (Guid id, ReviewRequest? req, ChangeRequestService svc, CancellationToken ct) =>
            svc.ApproveAsync(id, req?.Comment, ct));

        g.MapPost("/{id:guid}/reject", (Guid id, ReviewRequest? req, ChangeRequestService svc, CancellationToken ct) =>
            svc.RejectAsync(id, req?.Comment, ct));

        g.MapPost("/{id:guid}/cancel", (Guid id, ChangeRequestService svc, CancellationToken ct) => svc.CancelAsync(id, ct));
    }
}

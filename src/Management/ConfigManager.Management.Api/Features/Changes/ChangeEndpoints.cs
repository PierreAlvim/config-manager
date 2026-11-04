using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Domain;
using ConfigManager.Management.Api.Services;

namespace ConfigManager.Management.Api.Features.Changes;

public sealed record SubmitChangeRequest(string? Title, string? Description, bool ForceChangeRequest, List<ChangeOperation>? Operations);
public sealed record PromoteRequest(string FromEnvironment, List<string>? FlagKeys, string? Title);
public sealed record RollbackRequest(string? Reason);

public static class ChangeEndpoints
{
    public static void MapChanges(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/namespaces/{ns}/environments/{env}");

        g.MapGet("/config", async (string ns, string env, NamespaceAuthorizer auth, PublishingService publishing, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            await publishing.EnsureEnvironmentAsync(env, ct);
            return await publishing.GetLatestSnapshotAsync(ns, env, ct);
        });

        g.MapPost("/changes", async (string ns, string env, SubmitChangeRequest req, ChangeService changes, CancellationToken ct) =>
            ToResult(await changes.SubmitAsync(ns, env,
                new ChangeSubmission(req.Title ?? "", req.Description, req.ForceChangeRequest, req.Operations ?? []), ct)));

        g.MapPost("/promote", async (string ns, string env, PromoteRequest req, ChangeService changes, CancellationToken ct) =>
            ToResult(await changes.PromoteAsync(ns, env, req.FromEnvironment, req.FlagKeys, req.Title, ct)));
    }

    public static IResult ToResult(SubmitResult r) => r.ChangeRequest is { } cr
        ? Results.Json(new { outcome = "changeRequestCreated", changeRequest = cr }, statusCode: StatusCodes.Status202Accepted)
        : Results.Ok(new { outcome = "applied", version = r.AppliedVersion });
}

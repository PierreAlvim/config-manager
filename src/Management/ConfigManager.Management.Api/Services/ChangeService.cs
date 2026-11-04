using System.Text.Json;
using ConfigManager.Contracts;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Services;

/// <summary>Entry point for changes, promotions and rollbacks: validates, applies the policy, applies or opens a CR.</summary>
public sealed class ChangeService(
    AppDbContext db, PublishingService publishing, NamespaceAuthorizer auth, ChangeRequestService requests, ICurrentUser user)
{
    public async Task<SubmitResult> SubmitAsync(string ns, string env, ChangeSubmission submission, CancellationToken ct)
    {
        await auth.RequireAsync(ns, NamespaceRole.Editor, ct);
        await publishing.EnsureEnvironmentAsync(env, ct);
        if (string.IsNullOrWhiteSpace(submission.Title))
            throw ApiException.Validation(["Title is required."]);
        return await SubmitCoreAsync(ns, env, submission, ct);
    }

    public async Task<SubmitResult> PromoteAsync(string ns, string env, string fromEnvironment, IReadOnlyCollection<string>? flagKeys, string? title, CancellationToken ct)
    {
        await auth.RequireAsync(ns, NamespaceRole.Editor, ct);
        await publishing.EnsureEnvironmentAsync(env, ct);
        await publishing.EnsureEnvironmentAsync(fromEnvironment, ct);
        if (fromEnvironment == env)
            throw ApiException.BadRequest("Source and target environments must differ.");

        var source = (await publishing.GetLatestSnapshotAsync(ns, fromEnvironment, ct)).Flags;
        var target = await publishing.GetCurrentFlagsAsync(ns, env, ct);
        var plan = ChangePlanner.Promote(source, target, flagKeys);
        if (plan.MissingInSource.Count > 0)
            throw ApiException.Validation(plan.MissingInSource.Select(k => $"Flag '{k}' is not configured in '{fromEnvironment}'.").ToList());
        if (plan.Operations.Count == 0)
            throw ApiException.BadRequest($"Nothing to promote: '{env}' already matches '{fromEnvironment}'.");

        var submission = new ChangeSubmission(
            string.IsNullOrWhiteSpace(title) ? $"Promote {fromEnvironment} to {env}" : title,
            $"Promotion of {plan.Operations.Count} flag(s) from {fromEnvironment}", false, plan.Operations);
        return await SubmitCoreAsync(ns, env, submission, ct);
    }

    public async Task<SubmitResult> RollbackAsync(string ns, string env, long version, string? reason, CancellationToken ct)
    {
        await auth.RequireAsync(ns, NamespaceRole.Editor, ct);
        await publishing.EnsureEnvironmentAsync(env, ct);
        var row = await publishing.GetSnapshotRowAsync(ns, env, version, ct)
                  ?? throw ApiException.NotFound($"Version {version} of {ns}/{env} does not exist.");

        var target = SnapshotJson.Deserialize(row.Content).Flags;
        var current = await publishing.GetCurrentFlagsAsync(ns, env, ct);
        var ops = ChangePlanner.Rollback(target, current);
        if (ops.Count == 0)
            throw ApiException.BadRequest($"Nothing to roll back: current configuration already equals version {version}.");

        var submission = new ChangeSubmission($"Rollback to version {version}", reason, false, ops);
        return await SubmitCoreAsync(ns, env, submission, ct);
    }

    private async Task<SubmitResult> SubmitCoreAsync(string ns, string env, ChangeSubmission s, CancellationToken ct)
    {
        await publishing.ValidateAsync(ns, s.Operations, ct);
        var policy = await db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.NamespaceKey == ns && p.EnvironmentKey == env, ct);

        if (policy is { RequireChangeRequest: true } || s.ForceChangeRequest)
        {
            var now = DateTimeOffset.UtcNow;
            var cr = new ChangeRequest
            {
                Id = Guid.NewGuid(), NamespaceKey = ns, EnvironmentKey = env, Title = s.Title,
                Description = s.Description ?? "", Status = ChangeRequestStatus.Pending, Author = user.Username,
                CreatedAt = now, UpdatedAt = now, BaseVersion = await publishing.LatestVersionAsync(ns, env, ct),
                RequiredApprovals = Math.Max(1, policy?.RequiredApprovals ?? 1),
                Operations = JsonSerializer.Serialize(s.Operations, SnapshotJson.Options),
            };
            db.ChangeRequests.Add(cr);
            db.AddAudit(user.Username, "changerequest.created", ns, env, JsonSerializer.Serialize(new { cr.Id, cr.Title }));
            await db.SaveChangesAsync(ct);
            return new SubmitResult(null, await requests.BuildDetailAsync(cr, ct));
        }

        var version = await publishing.PublishAsync(ns, env, s.Operations, user.Username, s.Title, null, "change.applied", ct);
        return new SubmitResult(version, null);
    }
}

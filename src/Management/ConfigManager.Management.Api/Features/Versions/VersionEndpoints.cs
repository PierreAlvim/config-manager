using ConfigManager.Contracts;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using ConfigManager.Management.Api.Features.Changes;
using ConfigManager.Management.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Features.Versions;

public static class VersionEndpoints
{
    public static void MapVersions(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/namespaces/{ns}/environments/{env}/versions");

        g.MapGet("/", async (string ns, string env, AppDbContext db, NamespaceAuthorizer auth, PublishingService publishing, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            await publishing.EnsureEnvironmentAsync(env, ct);
            return await db.PublishedSnapshots.AsNoTracking()
                .Where(s => s.NamespaceKey == ns && s.EnvironmentKey == env)
                .OrderByDescending(s => s.Version)
                .Select(s => new { s.Version, s.PublishedAt, s.PublishedBy, s.Message, s.ChangeRequestId })
                .ToListAsync(ct);
        });

        g.MapGet("/{v:long}", async (string ns, string env, long v, NamespaceAuthorizer auth, PublishingService publishing, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            await publishing.EnsureEnvironmentAsync(env, ct);
            var row = await publishing.GetSnapshotRowAsync(ns, env, v, ct)
                      ?? throw ApiException.NotFound($"Version {v} of {ns}/{env} does not exist.");
            return new
            {
                row.Version, row.PublishedAt, row.PublishedBy, row.Message, row.ChangeRequestId,
                snapshot = SnapshotJson.Deserialize(row.Content),
            };
        });

        g.MapPost("/{v:long}/rollback", async (string ns, string env, long v, RollbackRequest? req, ChangeService changes, CancellationToken ct) =>
            ChangeEndpoints.ToResult(await changes.RollbackAsync(ns, env, v, req?.Reason, ct)));
    }
}

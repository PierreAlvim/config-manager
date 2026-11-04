using ConfigManager.Contracts;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Features.Environments;

public sealed record EnvironmentRequest(string Key, string? Name, int SortOrder);

public static class EnvironmentEndpoints
{
    public static void MapEnvironments(this RouteGroupBuilder api)
    {
        api.MapGet("/environments", async (AppDbContext db, CancellationToken ct) =>
            await db.Environments.AsNoTracking().OrderBy(e => e.SortOrder).ThenBy(e => e.Key)
                .Select(e => new { e.Key, e.Name, e.SortOrder }).ToListAsync(ct));

        api.MapPost("/environments", async (EnvironmentRequest req, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            auth.RequireAdmin();
            if (!KeyRules.IsValidKey(req.Key)) throw ApiException.Validation([$"Invalid environment key '{req.Key}'."]);
            if (await db.Environments.AnyAsync(e => e.Key == req.Key, ct))
                throw ApiException.Conflict($"Environment '{req.Key}' already exists.");

            db.Environments.Add(new EnvironmentEntity { Key = req.Key, Name = string.IsNullOrWhiteSpace(req.Name) ? req.Key : req.Name, SortOrder = req.SortOrder });
            foreach (var ns in await db.Namespaces.Select(n => n.Key).ToListAsync(ct))
                db.Policies.Add(new NamespacePolicy { NamespaceKey = ns, EnvironmentKey = req.Key, RequireChangeRequest = false, RequiredApprovals = 1 });
            db.AddAudit(user.Username, "environment.created", details: req.Key);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/environments/{req.Key}", new { key = req.Key, name = req.Name ?? req.Key, req.SortOrder });
        });
    }
}

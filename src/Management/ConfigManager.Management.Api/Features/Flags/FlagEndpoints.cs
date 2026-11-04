using ConfigManager.Contracts;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Features.Flags;

public sealed record CreateFlagRequest(string Key, string? Name, string? Description, FlagType Type);
public sealed record UpdateFlagRequest(string? Name, string? Description);

public static class FlagEndpoints
{
    public static void MapFlags(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/namespaces/{ns}/flags");

        g.MapGet("/", async (string ns, AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            var envs = await db.Environments.AsNoTracking().OrderBy(e => e.SortOrder).Select(e => e.Key).ToListAsync(ct);
            var flags = await db.Flags.AsNoTracking().Where(f => f.NamespaceKey == ns).OrderBy(f => f.Key).ToListAsync(ct);
            var configs = (await db.FlagConfigs.AsNoTracking().Where(c => c.NamespaceKey == ns).ToListAsync(ct))
                .ToDictionary(c => (c.FlagKey, c.EnvironmentKey), c => SnapshotJson.DeserializeFlag(c.Content));

            return flags.Select(f => new
            {
                f.Key, f.Name, f.Description, type = f.Type,
                environments = envs.ToDictionary(e => e, e => configs.TryGetValue((f.Key, e), out var d)
                    ? new { enabled = d.Enabled, defaultVariant = d.DefaultVariant, rulesCount = d.Rules.Count }
                    : null),
            });
        });

        g.MapPost("/", async (string ns, CreateFlagRequest req, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Editor, ct);
            if (!KeyRules.IsValidKey(req.Key)) throw ApiException.Validation([$"Invalid flag key '{req.Key}'."]);
            if (await db.Flags.AnyAsync(f => f.NamespaceKey == ns && f.Key == req.Key, ct))
                throw ApiException.Conflict($"Flag '{req.Key}' already exists.");

            var flag = new FlagEntity
            {
                NamespaceKey = ns, Key = req.Key, Name = string.IsNullOrWhiteSpace(req.Name) ? req.Key : req.Name,
                Description = req.Description ?? "", Type = req.Type, CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Flags.Add(flag);
            db.AddAudit(user.Username, "flag.created", ns, details: $"{req.Key} ({req.Type.ToString().ToLowerInvariant()})");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/namespaces/{ns}/flags/{flag.Key}", new { flag.Key, flag.Name, flag.Description, type = flag.Type });
        });

        g.MapGet("/{key}", async (string ns, string key, AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            var flag = await FindAsync(db, ns, key, ct, tracking: false);
            var envs = await db.Environments.AsNoTracking().OrderBy(e => e.SortOrder).Select(e => e.Key).ToListAsync(ct);
            var configs = (await db.FlagConfigs.AsNoTracking().Where(c => c.NamespaceKey == ns && c.FlagKey == key).ToListAsync(ct))
                .ToDictionary(c => c.EnvironmentKey, c => SnapshotJson.DeserializeFlag(c.Content));
            return new
            {
                flag.Key, flag.Name, flag.Description, type = flag.Type, flag.CreatedAt,
                configs = envs.ToDictionary(e => e, e => configs.GetValueOrDefault(e)),
            };
        });

        g.MapPut("/{key}", async (string ns, string key, UpdateFlagRequest req, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Editor, ct);
            var flag = await FindAsync(db, ns, key, ct);
            if (!string.IsNullOrWhiteSpace(req.Name)) flag.Name = req.Name;
            flag.Description = req.Description ?? flag.Description;
            db.AddAudit(user.Username, "flag.updated", ns, details: key);
            await db.SaveChangesAsync(ct);
            return new { flag.Key, flag.Name, flag.Description, type = flag.Type };
        });

        g.MapDelete("/{key}", async (string ns, string key, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Editor, ct);
            var flag = await FindAsync(db, ns, key, ct);
            if (await db.FlagConfigs.AnyAsync(c => c.NamespaceKey == ns && c.FlagKey == key, ct))
                throw ApiException.Conflict($"Flag '{key}' is still configured in at least one environment; remove it from all environments first.");
            db.Flags.Remove(flag);
            db.AddAudit(user.Username, "flag.deleted", ns, details: key);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static async Task<FlagEntity> FindAsync(AppDbContext db, string ns, string key, CancellationToken ct, bool tracking = true)
    {
        var q = tracking ? db.Flags.AsTracking() : db.Flags.AsNoTracking();
        return await q.FirstOrDefaultAsync(f => f.NamespaceKey == ns && f.Key == key, ct)
               ?? throw ApiException.NotFound($"Flag '{key}' does not exist in namespace '{ns}'.");
    }
}

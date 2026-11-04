using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using ConfigManager.Management.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Features.Access;

public sealed record ServiceAccessRequest(string ClientId, string Environment);

public static class ServiceAccessEndpoints
{
    public static void MapServiceAccess(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/namespaces/{ns}/service-access");

        g.MapGet("/", async (string ns, AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            return await db.AccessGrants.AsNoTracking().Where(a => a.NamespaceKey == ns)
                .OrderBy(a => a.ClientId).ThenBy(a => a.EnvironmentKey)
                .Select(a => new { a.Id, a.ClientId, environment = a.EnvironmentKey, a.CreatedAt, a.CreatedBy }).ToListAsync(ct);
        });

        g.MapPost("/", async (string ns, ServiceAccessRequest req, AppDbContext db, NamespaceAuthorizer auth, PublishingService publishing, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(req.ClientId)) errors.Add("clientId is required.");
            if (string.IsNullOrWhiteSpace(req.Environment)) errors.Add("environment is required ('*' for all).");
            if (errors.Count > 0) throw ApiException.Validation(errors);
            if (req.Environment != "*" && !await db.Environments.AnyAsync(e => e.Key == req.Environment, ct))
                throw ApiException.Validation([$"Environment '{req.Environment}' does not exist."]);

            var clientId = req.ClientId.Trim();
            if (await db.AccessGrants.AnyAsync(a => a.ClientId == clientId && a.NamespaceKey == ns && a.EnvironmentKey == req.Environment, ct))
                throw ApiException.Conflict("This access grant already exists.");

            var grant = new ServiceAccessGrant
            {
                Id = Guid.NewGuid(), ClientId = clientId, NamespaceKey = ns, EnvironmentKey = req.Environment,
                CreatedAt = DateTimeOffset.UtcNow, CreatedBy = user.Username,
            };
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.AccessGrants.Add(grant);
            db.AddAudit(user.Username, "serviceaccess.granted", ns, req.Environment, clientId);
            await db.SaveChangesAsync(ct);
            await publishing.NotifyAccessChangedAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Created($"/api/namespaces/{ns}/service-access/{grant.Id}",
                new { grant.Id, grant.ClientId, environment = grant.EnvironmentKey, grant.CreatedAt, grant.CreatedBy });
        });

        g.MapDelete("/{id:guid}", async (string ns, Guid id, AppDbContext db, NamespaceAuthorizer auth, PublishingService publishing, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            var grant = await db.AccessGrants.FirstOrDefaultAsync(a => a.Id == id && a.NamespaceKey == ns, ct)
                        ?? throw ApiException.NotFound("Access grant not found.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.AccessGrants.Remove(grant);
            db.AddAudit(user.Username, "serviceaccess.revoked", ns, grant.EnvironmentKey, grant.ClientId);
            await db.SaveChangesAsync(ct);
            await publishing.NotifyAccessChangedAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });
    }
}

using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Features.Audit;

public static class AuditEndpoints
{
    public static void MapAudit(this RouteGroupBuilder api)
    {
        api.MapGet("/audit", async (string? @namespace, int? take, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            var query = db.AuditEntries.AsNoTracking().AsQueryable();
            if (!string.IsNullOrEmpty(@namespace))
            {
                await auth.RequireAsync(@namespace, NamespaceRole.Viewer, ct);
                query = query.Where(a => a.NamespaceKey == @namespace);
            }
            else if (!user.IsAdmin)
            {
                var visible = (await auth.GetRolesAsync(ct)).Keys.ToList();
                query = query.Where(a => a.NamespaceKey != null && visible.Contains(a.NamespaceKey));
            }
            return await query.OrderByDescending(a => a.At).Take(Math.Clamp(take ?? 100, 1, 1000))
                .Select(a => new { a.At, a.Actor, a.Action, @namespace = a.NamespaceKey, environment = a.EnvironmentKey, a.Details })
                .ToListAsync(ct);
        });
    }
}

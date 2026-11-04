using ConfigManager.Contracts;
using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using ConfigManager.Management.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Features.Namespaces;

public sealed record NamespaceRequest(string? Key, string? Name, string? Description);
public sealed record PolicyRequest(bool RequireChangeRequest, int RequiredApprovals);
public sealed record MemberRequest(string SubjectType, string Subject, string Role);

public static class NamespaceEndpoints
{
    public static void MapNamespaces(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/namespaces");

        g.MapGet("/", async (AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            var roles = await auth.GetRolesAsync(ct);
            var keys = roles.Keys.ToList();
            var list = await db.Namespaces.AsNoTracking().Where(n => keys.Contains(n.Key)).OrderBy(n => n.Key).ToListAsync(ct);
            return list.Select(n => new { n.Key, n.Name, n.Description, myRole = roles[n.Key].ToApi() });
        });

        g.MapPost("/", async (NamespaceRequest req, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            auth.RequireAdmin();
            if (!KeyRules.IsValidKey(req.Key)) throw ApiException.Validation([$"Invalid namespace key '{req.Key}'."]);
            if (await db.Namespaces.AnyAsync(n => n.Key == req.Key, ct))
                throw ApiException.Conflict($"Namespace '{req.Key}' already exists.");

            db.Namespaces.Add(new NamespaceEntity
            {
                Key = req.Key!, Name = string.IsNullOrWhiteSpace(req.Name) ? req.Key! : req.Name, Description = req.Description ?? "",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            foreach (var env in await db.Environments.Select(e => e.Key).ToListAsync(ct))
                db.Policies.Add(new NamespacePolicy { NamespaceKey = req.Key!, EnvironmentKey = env, RequireChangeRequest = env == "prod", RequiredApprovals = 1 });
            db.AddAudit(user.Username, "namespace.created", req.Key);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/namespaces/{req.Key}", new { key = req.Key, name = req.Name ?? req.Key, description = req.Description ?? "", myRole = "owner" });
        });

        g.MapGet("/{ns}", async (string ns, AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            var role = await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            var n = await db.Namespaces.AsNoTracking().SingleAsync(x => x.Key == ns, ct);
            return new { n.Key, n.Name, n.Description, myRole = role.ToApi() };
        });

        g.MapPut("/{ns}", async (string ns, NamespaceRequest req, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            var role = await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            var n = await db.Namespaces.SingleAsync(x => x.Key == ns, ct);
            if (!string.IsNullOrWhiteSpace(req.Name)) n.Name = req.Name;
            n.Description = req.Description ?? n.Description;
            db.AddAudit(user.Username, "namespace.updated", ns);
            await db.SaveChangesAsync(ct);
            return new { n.Key, n.Name, n.Description, myRole = role.ToApi() };
        });

        g.MapDelete("/{ns}", async (string ns, AppDbContext db, PublishingService publishing, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            auth.RequireAdmin();
            if (!await db.Namespaces.AnyAsync(n => n.Key == ns, ct)) throw ApiException.NotFound($"Namespace '{ns}' does not exist.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var crIds = db.ChangeRequests.Where(c => c.NamespaceKey == ns).Select(c => c.Id);
            await db.Reviews.Where(r => crIds.Contains(r.ChangeRequestId)).ExecuteDeleteAsync(ct);
            await db.ChangeRequests.Where(c => c.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.FlagConfigs.Where(c => c.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.Flags.Where(f => f.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.PublishedSnapshots.Where(s => s.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.AccessGrants.Where(a => a.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.RoleBindings.Where(b => b.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.Policies.Where(p => p.NamespaceKey == ns).ExecuteDeleteAsync(ct);
            await db.Namespaces.Where(n => n.Key == ns).ExecuteDeleteAsync(ct);
            db.AddAudit(user.Username, "namespace.deleted", ns);
            await db.SaveChangesAsync(ct);
            await publishing.NotifyAccessChangedAsync(ct);
            await tx.CommitAsync(ct);
            return Results.NoContent();
        });

        // Policies
        g.MapGet("/{ns}/policies", async (string ns, AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Viewer, ct);
            var envs = await db.Environments.AsNoTracking().OrderBy(e => e.SortOrder).Select(e => e.Key).ToListAsync(ct);
            var policies = await db.Policies.AsNoTracking().Where(p => p.NamespaceKey == ns).ToDictionaryAsync(p => p.EnvironmentKey, ct);
            return envs.Select(e => new
            {
                environment = e,
                requireChangeRequest = policies.TryGetValue(e, out var p) && p.RequireChangeRequest,
                requiredApprovals = p?.RequiredApprovals ?? 1,
            });
        });

        g.MapPut("/{ns}/policies/{env}", async (string ns, string env, PolicyRequest req, AppDbContext db, NamespaceAuthorizer auth, PublishingService publishing, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            await publishing.EnsureEnvironmentAsync(env, ct);
            if (req.RequiredApprovals < 1) throw ApiException.Validation(["requiredApprovals must be at least 1."]);
            var p = await db.Policies.FindAsync([ns, env], ct);
            if (p is null) db.Policies.Add(p = new NamespacePolicy { NamespaceKey = ns, EnvironmentKey = env });
            p.RequireChangeRequest = req.RequireChangeRequest;
            p.RequiredApprovals = req.RequiredApprovals;
            db.AddAudit(user.Username, "policy.updated", ns, env, $"requireChangeRequest={req.RequireChangeRequest}, requiredApprovals={req.RequiredApprovals}");
            await db.SaveChangesAsync(ct);
            return new { environment = env, p.RequireChangeRequest, p.RequiredApprovals };
        });

        // Members
        g.MapGet("/{ns}/members", async (string ns, AppDbContext db, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            var list = await db.RoleBindings.AsNoTracking().Where(b => b.NamespaceKey == ns).OrderBy(b => b.SubjectType).ThenBy(b => b.Subject).ToListAsync(ct);
            return list.Select(b => new { b.Id, b.SubjectType, b.Subject, role = b.Role.ToApi() });
        });

        g.MapPost("/{ns}/members", async (string ns, MemberRequest req, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            var errors = new List<string>();
            if (req.SubjectType is not (SubjectTypes.User or SubjectTypes.Group)) errors.Add("subjectType must be 'user' or 'group'.");
            if (string.IsNullOrWhiteSpace(req.Subject)) errors.Add("subject is required.");
            if (!RoleResolver.TryParse(req.Role, out var role)) errors.Add("role must be viewer, editor, approver or owner.");
            if (errors.Count > 0) throw ApiException.Validation(errors);

            var subject = req.Subject.Trim().TrimStart('/');
            var b = await db.RoleBindings.FirstOrDefaultAsync(x => x.NamespaceKey == ns && x.SubjectType == req.SubjectType && x.Subject == subject, ct);
            if (b is null)
                db.RoleBindings.Add(b = new RoleBinding { Id = Guid.NewGuid(), NamespaceKey = ns, SubjectType = req.SubjectType, Subject = subject });
            b.Role = role;
            db.AddAudit(user.Username, "member.upserted", ns, details: $"{req.SubjectType}:{subject}={role.ToApi()}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/namespaces/{ns}/members/{b.Id}", new { b.Id, b.SubjectType, b.Subject, role = role.ToApi() });
        });

        g.MapDelete("/{ns}/members/{id:guid}", async (string ns, Guid id, AppDbContext db, NamespaceAuthorizer auth, ICurrentUser user, CancellationToken ct) =>
        {
            await auth.RequireAsync(ns, NamespaceRole.Owner, ct);
            var b = await db.RoleBindings.FirstOrDefaultAsync(x => x.Id == id && x.NamespaceKey == ns, ct)
                    ?? throw ApiException.NotFound("Member not found.");
            db.RoleBindings.Remove(b);
            db.AddAudit(user.Username, "member.removed", ns, details: $"{b.SubjectType}:{b.Subject}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}

using ConfigManager.Management.Api.Common;
using ConfigManager.Management.Api.Data;
using ConfigManager.Management.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ConfigManager.Management.Api.Auth;

/// <summary>Resolves the current user's effective namespace role from the stored bindings.</summary>
public sealed class NamespaceAuthorizer(AppDbContext db, ICurrentUser user)
{
    public async Task<NamespaceRole?> GetRoleAsync(string ns, CancellationToken ct)
    {
        if (user.IsAdmin) return NamespaceRole.Owner;
        var bindings = await db.RoleBindings.AsNoTracking().Where(b => b.NamespaceKey == ns).ToListAsync(ct);
        return RoleResolver.Resolve(bindings.Select(ToInfo), user.Username, user.Groups, false);
    }

    /// <summary>Roles of the current user for every namespace where they have one (admin: owner everywhere).</summary>
    public async Task<Dictionary<string, NamespaceRole>> GetRolesAsync(CancellationToken ct)
    {
        if (user.IsAdmin)
        {
            var all = await db.Namespaces.AsNoTracking().Select(n => n.Key).ToListAsync(ct);
            return all.ToDictionary(k => k, _ => NamespaceRole.Owner);
        }
        var groups = user.Groups.ToList();
        var username = user.Username;
        var bindings = await db.RoleBindings.AsNoTracking()
            .Where(b => b.Subject == username || groups.Contains(b.Subject))
            .ToListAsync(ct);
        var result = new Dictionary<string, NamespaceRole>();
        foreach (var g in bindings.GroupBy(b => b.NamespaceKey))
            if (RoleResolver.Resolve(g.Select(ToInfo), username, groups, false) is { } role)
                result[g.Key] = role;
        return result;
    }

    /// <summary>404 for unknown namespace, 403 when the user has no role or less than <paramref name="min"/>.</summary>
    public async Task<NamespaceRole> RequireAsync(string ns, NamespaceRole min, CancellationToken ct)
    {
        if (!await db.Namespaces.AnyAsync(n => n.Key == ns, ct))
            throw ApiException.NotFound($"Namespace '{ns}' does not exist.");
        var role = await GetRoleAsync(ns, ct);
        if (role is null || role < min)
            throw ApiException.Forbidden($"Requires role '{min.ToApi()}' in namespace '{ns}'.");
        return role.Value;
    }

    public void RequireAdmin()
    {
        if (!user.IsAdmin) throw ApiException.Forbidden("Requires the cm-admin realm role.");
    }

    private static RoleBindingInfo ToInfo(RoleBinding b) => new(b.SubjectType, b.Subject, b.Role);
}

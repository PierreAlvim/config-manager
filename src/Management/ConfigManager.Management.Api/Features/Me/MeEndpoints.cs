using ConfigManager.Management.Api.Auth;
using ConfigManager.Management.Api.Domain;

namespace ConfigManager.Management.Api.Features.Me;

public static class MeEndpoints
{
    public static void MapMe(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (ICurrentUser user, NamespaceAuthorizer auth, CancellationToken ct) =>
        {
            var roles = await auth.GetRolesAsync(ct);
            return new
            {
                username = user.Username,
                name = user.Name,
                email = user.Email,
                isAdmin = user.IsAdmin,
                groups = user.Groups,
                namespaceRoles = roles.ToDictionary(r => r.Key, r => r.Value.ToApi()),
            };
        });
    }
}

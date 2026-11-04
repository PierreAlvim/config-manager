using System.Security.Claims;
using System.Text.Json;

namespace ConfigManager.Management.Api.Auth;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    string Username { get; }
    string? Name { get; }
    string? Email { get; }
    IReadOnlyList<string> Groups { get; }
    bool IsAdmin { get; }
}

/// <summary>Reads Keycloak-shaped claims (preferred_username, name, email, groups, realm_access) from a principal.</summary>
public sealed class CurrentUser : ICurrentUser
{
    public const string AdminRole = "cm-admin";

    public CurrentUser(ClaimsPrincipal principal)
    {
        IsAuthenticated = principal.Identity?.IsAuthenticated == true;
        Username = principal.FindFirstValue("preferred_username")
                   ?? principal.FindFirstValue("sub")
                   ?? principal.Identity?.Name
                   ?? "";
        Name = principal.FindFirstValue("name");
        Email = principal.FindFirstValue("email");
        Groups = principal.FindAll("groups")
            .SelectMany(c => ExpandGroups(c.Value))
            .Select(g => g.Trim().TrimStart('/'))
            .Where(g => g.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        IsAdmin = principal.FindAll("realm_access").Any(c => RealmRoles(c.Value).Contains(AdminRole, StringComparer.Ordinal));
    }

    public bool IsAuthenticated { get; }
    public string Username { get; }
    public string? Name { get; }
    public string? Email { get; }
    public IReadOnlyList<string> Groups { get; }
    public bool IsAdmin { get; }

    private static IEnumerable<string> ExpandGroups(string value)
    {
        var v = value.Trim();
        if (!v.StartsWith('[')) return [v];
        try
        {
            return JsonSerializer.Deserialize<string[]>(v) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IEnumerable<string> RealmRoles(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("roles", out var roles)
                && roles.ValueKind == JsonValueKind.Array)
                return roles.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.String).Select(r => r.GetString()!).ToList();
        }
        catch (JsonException)
        {
        }
        return [];
    }
}

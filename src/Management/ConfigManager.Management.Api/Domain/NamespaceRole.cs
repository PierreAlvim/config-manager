namespace ConfigManager.Management.Api.Domain;

/// <summary>Hierarchical namespace roles: viewer &lt; editor &lt; approver &lt; owner.</summary>
public enum NamespaceRole { Viewer = 1, Editor = 2, Approver = 3, Owner = 4 }

public enum ChangeRequestStatus { Pending, Applied, Rejected, Cancelled, Conflict }

public static class SubjectTypes
{
    public const string User = "user";
    public const string Group = "group";
}

public static class ReviewDecisions
{
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}

public sealed record RoleBindingInfo(string SubjectType, string Subject, NamespaceRole Role);

public static class RoleResolver
{
    /// <summary>Effective role = max over bindings matching the user or any of their groups; admin = owner.</summary>
    public static NamespaceRole? Resolve(IEnumerable<RoleBindingInfo> bindings, string username, IEnumerable<string> groups, bool isAdmin)
    {
        if (isAdmin) return NamespaceRole.Owner;
        var groupSet = groups.ToHashSet(StringComparer.Ordinal);
        NamespaceRole? best = null;
        foreach (var b in bindings)
        {
            var matches = b.SubjectType == SubjectTypes.User
                ? string.Equals(b.Subject, username, StringComparison.Ordinal)
                : b.SubjectType == SubjectTypes.Group && groupSet.Contains(b.Subject);
            if (matches && (best is null || b.Role > best)) best = b.Role;
        }
        return best;
    }

    public static string ToApi(this NamespaceRole role) => role.ToString().ToLowerInvariant();

    public static bool TryParse(string? value, out NamespaceRole role) =>
        Enum.TryParse(value, ignoreCase: true, out role) && Enum.IsDefined(role);
}

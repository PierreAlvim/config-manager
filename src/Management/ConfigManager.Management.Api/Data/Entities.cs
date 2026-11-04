using ConfigManager.Contracts;
using ConfigManager.Management.Api.Domain;

namespace ConfigManager.Management.Api.Data;

public class EnvironmentEntity
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class NamespaceEntity
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class NamespacePolicy
{
    public string NamespaceKey { get; set; } = "";
    public string EnvironmentKey { get; set; } = "";
    public bool RequireChangeRequest { get; set; }
    public int RequiredApprovals { get; set; } = 1;
}

public class RoleBinding
{
    public Guid Id { get; set; }
    public string NamespaceKey { get; set; } = "";
    public string SubjectType { get; set; } = SubjectTypes.User;
    public string Subject { get; set; } = "";
    public NamespaceRole Role { get; set; }
}

public class FlagEntity
{
    public string NamespaceKey { get; set; } = "";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public FlagType Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class FlagEnvironmentConfig
{
    public string NamespaceKey { get; set; } = "";
    public string EnvironmentKey { get; set; } = "";
    public string FlagKey { get; set; } = "";
    /// <summary>FlagDefinition serialized with SnapshotJson.Options (jsonb).</summary>
    public string Content { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Contract table read by the Distribution API.</summary>
public class PublishedSnapshot
{
    public Guid Id { get; set; }
    public string NamespaceKey { get; set; } = "";
    public string EnvironmentKey { get; set; } = "";
    public long Version { get; set; }
    public string Content { get; set; } = "{}";
    public DateTimeOffset PublishedAt { get; set; }
    public string PublishedBy { get; set; } = "";
    public Guid? ChangeRequestId { get; set; }
    public string? Message { get; set; }
}

/// <summary>Contract table read by the Distribution API.</summary>
public class ServiceAccessGrant
{
    public Guid Id { get; set; }
    public string ClientId { get; set; } = "";
    public string NamespaceKey { get; set; } = "";
    public string EnvironmentKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
}

public class ChangeRequest
{
    public Guid Id { get; set; }
    public string NamespaceKey { get; set; } = "";
    public string EnvironmentKey { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public ChangeRequestStatus Status { get; set; }
    public string Author { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long BaseVersion { get; set; }
    public long? AppliedVersion { get; set; }
    public int RequiredApprovals { get; set; } = 1;
    /// <summary>List&lt;ChangeOperation&gt; as jsonb.</summary>
    public string Operations { get; set; } = "[]";
}

public class ChangeRequestReview
{
    public Guid Id { get; set; }
    public Guid ChangeRequestId { get; set; }
    public string Reviewer { get; set; } = "";
    public string Decision { get; set; } = ReviewDecisions.Approved;
    public string? Comment { get; set; }
    public DateTimeOffset At { get; set; }
}

public class AuditEntry
{
    public Guid Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? NamespaceKey { get; set; }
    public string? EnvironmentKey { get; set; }
    public string? Details { get; set; }
}

namespace ConfigManager.Distribution.Api;

public sealed class DistributionOptions
{
    public const string SectionName = "Distribution";

    /// <summary>Safety poll comparing max versions in the database against the cache.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Interval of SSE <c>: keepalive</c> comments.</summary>
    public TimeSpan KeepAliveInterval { get; set; } = TimeSpan.FromSeconds(15);
}

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string? Authority { get; set; }
    public string? MetadataAddress { get; set; }
    public string Audience { get; set; } = "config-distribution";
}

/// <summary>Raw Postgres connection string (Npgsql data sources redact the password).</summary>
public sealed record DatabaseOptions(string ConnectionString);

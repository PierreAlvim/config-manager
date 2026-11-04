namespace ConfigManager.OpenFeature;

/// <summary>How the provider keeps its local snapshot up to date.</summary>
public enum ConfigManagerMode
{
    /// <summary>Server-sent events with automatic reconnect (falls back to polling while the stream is failing).</summary>
    Streaming,

    /// <summary>Periodic conditional GET (ETag / If-None-Match).</summary>
    Polling,
}

/// <summary>Options of <see cref="ConfigManagerProvider"/>; bindable from the <c>ConfigManager</c> configuration section.</summary>
public sealed class ConfigManagerProviderOptions
{
    public const string SectionName = "ConfigManager";

    /// <summary>Base URL of the Distribution API, e.g. <c>http://localhost:5002</c>.</summary>
    public string DistributionUrl { get; set; } = "";

    public string Namespace { get; set; } = "";

    public string Environment { get; set; } = "";

    /// <summary>OIDC token endpoint used for the client-credentials grant.</summary>
    public string TokenEndpoint { get; set; } = "";

    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    public string? Scope { get; set; }

    public ConfigManagerMode Mode { get; set; } = ConfigManagerMode.Streaming;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Optional file holding the last-known-good snapshot (written on every update, read when the server is unreachable at startup).</summary>
    public string? CacheFilePath { get; set; }

    public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Throws <see cref="InvalidOperationException"/> when a required option is missing.</summary>
    public void Validate()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(DistributionUrl), DistributionUrl), (nameof(Namespace), Namespace), (nameof(Environment), Environment),
            (nameof(TokenEndpoint), TokenEndpoint), (nameof(ClientId), ClientId), (nameof(ClientSecret), ClientSecret),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"ConfigManager option '{name}' is required.");
        }

        if (PollInterval <= TimeSpan.Zero)
            throw new InvalidOperationException("ConfigManager option 'PollInterval' must be positive.");
        if (InitializationTimeout <= TimeSpan.Zero)
            throw new InvalidOperationException("ConfigManager option 'InitializationTimeout' must be positive.");
    }
}

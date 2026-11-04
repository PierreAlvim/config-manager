using ConfigManager.Distribution.Api.Caching;

namespace ConfigManager.Distribution.Api.Background;

/// <summary>Translates Postgres notifications into cache refreshes.</summary>
public sealed class NotificationDispatcher(SnapshotStore snapshots, AccessGrantStore grants, ILogger<NotificationDispatcher> logger)
{
    public const string ConfigPublishedChannel = "config_published";
    public const string AccessChangedChannel = "access_changed";

    public async Task DispatchAsync(string channel, string payload, CancellationToken ct)
    {
        switch (channel)
        {
            case ConfigPublishedChannel when TryParsePublished(payload, out var ns, out var env, out _):
                await snapshots.RefreshAsync(ns, env, ct);
                break;
            case ConfigPublishedChannel:
                logger.LogWarning("Ignoring malformed {Channel} payload '{Payload}'", channel, payload);
                break;
            case AccessChangedChannel:
                await grants.ReloadAsync(ct);
                break;
        }
    }

    /// <summary>Parses <c>namespace/environment/version</c>.</summary>
    public static bool TryParsePublished(string payload, out string ns, out string env, out long version)
    {
        ns = env = "";
        version = 0;
        var parts = payload.Split('/');
        if (parts.Length != 3 || parts[0].Length == 0 || parts[1].Length == 0 || !long.TryParse(parts[2], out version))
            return false;
        ns = parts[0];
        env = parts[1];
        return true;
    }
}

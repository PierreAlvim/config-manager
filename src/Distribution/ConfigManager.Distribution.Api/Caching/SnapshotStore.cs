using System.Collections.Concurrent;
using System.Text.Json;
using ConfigManager.Distribution.Api.Data;

namespace ConfigManager.Distribution.Api.Caching;

/// <summary>Latest snapshot per (namespace, environment), loaded lazily and kept fresh by notifications and polling.</summary>
public sealed class SnapshotStore(ISnapshotSource source, SnapshotBroadcaster broadcaster, ILogger<SnapshotStore> logger)
{
    private readonly ConcurrentDictionary<(string Ns, string Env), StoredSnapshot> _cache = new();
    private readonly Lock _gate = new();

    public bool IsCached(string ns, string env) => _cache.ContainsKey((ns, env));

    public async Task<StoredSnapshot?> GetAsync(string ns, string env, CancellationToken ct)
    {
        if (_cache.TryGetValue((ns, env), out var cached))
            return cached;

        var raw = await source.GetLatestAsync(ns, env, ct);
        if (raw is null || !TryApply(ns, env, raw))
            return _cache.GetValueOrDefault((ns, env));
        return _cache[(ns, env)];
    }

    /// <summary>Reloads a pair if (and only if) it is already cached; unseen pairs load lazily on first request.</summary>
    public async Task RefreshAsync(string ns, string env, CancellationToken ct)
    {
        if (!IsCached(ns, env)) return;
        var raw = await source.GetLatestAsync(ns, env, ct);
        if (raw is not null) TryApply(ns, env, raw);
    }

    /// <summary>Safety poll: refreshes every cached pair whose database version is ahead of the cache.</summary>
    public async Task SyncAsync(CancellationToken ct)
    {
        foreach (var pair in await source.GetLatestVersionsAsync(ct))
            if (_cache.TryGetValue((pair.Namespace, pair.Environment), out var cached) && pair.Version > cached.Version)
                await RefreshAsync(pair.Namespace, pair.Environment, ct);
    }

    /// <summary>Stores the snapshot if it is newer than the cached one and notifies subscribers.</summary>
    public bool TryApply(string ns, string env, RawSnapshot raw)
    {
        ConfigSnapshot snapshot;
        try
        {
            snapshot = SnapshotJson.Deserialize(raw.Json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Ignoring invalid snapshot JSON for {Namespace}/{Environment} v{Version}", ns, env, raw.Version);
            return false;
        }

        var stored = new StoredSnapshot(snapshot, raw.Json);
        lock (_gate)
        {
            if (_cache.TryGetValue((ns, env), out var existing) && existing.Version >= stored.Version)
                return false;
            _cache[(ns, env)] = stored;
        }

        logger.LogInformation("Snapshot {Namespace}/{Environment} now at v{Version}", ns, env, stored.Version);
        broadcaster.Publish(ns, env, stored);
        return true;
    }
}

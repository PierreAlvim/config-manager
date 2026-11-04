using ConfigManager.Distribution.Api.Background;
using ConfigManager.Distribution.Api.Caching;
using ConfigManager.Distribution.Api.Data;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfigManager.Distribution.Tests.Unit;

internal sealed class FakeSource : ISnapshotSource
{
    public Dictionary<(string, string), RawSnapshot> Snapshots { get; } = new();
    public List<AccessGrant> Grants { get; } = [];
    public int SnapshotReads;
    public int GrantReads;

    public void Put(string ns, string env, long version) =>
        Snapshots[(ns, env)] = new RawSnapshot(version, TestData.Json(ns, env, version));

    public Task<RawSnapshot?> GetLatestAsync(string ns, string env, CancellationToken ct)
    {
        Interlocked.Increment(ref SnapshotReads);
        return Task.FromResult(Snapshots.GetValueOrDefault((ns, env)));
    }

    public Task<IReadOnlyList<PairVersion>> GetLatestVersionsAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<PairVersion>>(
            Snapshots.Select(s => new PairVersion(s.Key.Item1, s.Key.Item2, s.Value.Version)).ToList());

    public Task<IReadOnlyList<AccessGrant>> GetGrantsAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref GrantReads);
        return Task.FromResult<IReadOnlyList<AccessGrant>>(Grants.ToList());
    }

    public Task PingAsync(CancellationToken ct) => Task.CompletedTask;
}

public class SnapshotStoreTests
{
    private readonly FakeSource _source = new();
    private readonly SnapshotBroadcaster _broadcaster = new();
    private readonly SnapshotStore _store;

    public SnapshotStoreTests() =>
        _store = new SnapshotStore(_source, _broadcaster, NullLogger<SnapshotStore>.Instance);

    [Fact]
    public async Task Loads_lazily_once_and_serves_raw_json()
    {
        _source.Put("orders", "dev", 1);

        var first = await _store.GetAsync("orders", "dev", default);
        var second = await _store.GetAsync("orders", "dev", default);

        Assert.Same(first, second);
        Assert.Equal(1, _source.SnapshotReads);
        Assert.Equal(_source.Snapshots[("orders", "dev")].Json, first!.Json);
        Assert.Equal(1, first.Version);
    }

    [Fact]
    public async Task Unknown_pair_returns_null_and_is_retried()
    {
        Assert.Null(await _store.GetAsync("orders", "dev", default));
        _source.Put("orders", "dev", 1);
        Assert.NotNull(await _store.GetAsync("orders", "dev", default));
    }

    [Fact]
    public async Task Refresh_ignores_uncached_pairs()
    {
        _source.Put("orders", "dev", 1);
        await _store.RefreshAsync("orders", "dev", default);
        Assert.Equal(0, _source.SnapshotReads);
    }

    [Fact]
    public async Task Refresh_updates_cache_and_broadcasts_to_matching_subscribers_only()
    {
        _source.Put("orders", "dev", 1);
        await _store.GetAsync("orders", "dev", default);
        using var match = _broadcaster.Subscribe("orders", "dev");
        using var other = _broadcaster.Subscribe("orders", "prod");

        _source.Put("orders", "dev", 2);
        await _store.RefreshAsync("orders", "dev", default);

        Assert.Equal(2, (await _store.GetAsync("orders", "dev", default))!.Version);
        Assert.True(match.Reader.TryRead(out var delivered));
        Assert.Equal(2, delivered!.Version);
        Assert.False(other.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Older_or_equal_versions_are_ignored()
    {
        _source.Put("orders", "dev", 5);
        await _store.GetAsync("orders", "dev", default);
        using var sub = _broadcaster.Subscribe("orders", "dev");

        Assert.False(_store.TryApply("orders", "dev", new RawSnapshot(5, TestData.Json("orders", "dev", 5))));
        Assert.False(_store.TryApply("orders", "dev", new RawSnapshot(4, TestData.Json("orders", "dev", 4))));

        Assert.False(sub.Reader.TryRead(out _));
        Assert.Equal(5, (await _store.GetAsync("orders", "dev", default))!.Version);
    }

    [Fact]
    public async Task Invalid_json_is_rejected_and_keeps_previous()
    {
        _source.Put("orders", "dev", 1);
        await _store.GetAsync("orders", "dev", default);

        Assert.False(_store.TryApply("orders", "dev", new RawSnapshot(2, "{not json")));
        Assert.Equal(1, (await _store.GetAsync("orders", "dev", default))!.Version);
    }

    [Fact]
    public async Task Sync_refreshes_only_stale_cached_pairs()
    {
        _source.Put("orders", "dev", 1);
        _source.Put("payments", "prod", 1);
        await _store.GetAsync("orders", "dev", default);
        var readsBefore = _source.SnapshotReads;

        _source.Put("orders", "dev", 2);
        _source.Put("payments", "prod", 2); // never requested: must not be loaded
        await _store.SyncAsync(default);

        Assert.Equal(readsBefore + 1, _source.SnapshotReads);
        Assert.Equal(2, (await _store.GetAsync("orders", "dev", default))!.Version);
        Assert.False(_store.IsCached("payments", "prod"));
    }

    [Fact]
    public async Task Slow_subscriber_only_keeps_latest()
    {
        using var sub = _broadcaster.Subscribe("orders", "dev");
        for (var v = 1; v <= 3; v++)
            _store.TryApply("orders", "dev", new RawSnapshot(v, TestData.Json("orders", "dev", v)));

        Assert.True(sub.Reader.TryRead(out var latest));
        Assert.Equal(3, latest!.Version);
        Assert.False(sub.Reader.TryRead(out _));
    }

    [Fact]
    public void Disposed_subscription_is_removed()
    {
        var sub = _broadcaster.Subscribe("a", "b");
        Assert.Equal(1, _broadcaster.SubscriberCount);
        sub.Dispose();
        Assert.Equal(0, _broadcaster.SubscriberCount);
    }
}

public class AccessGrantStoreTests
{
    [Fact]
    public async Task Loads_lazily_and_reloads_on_demand()
    {
        var source = new FakeSource();
        var store = new AccessGrantStore(source);

        Assert.False(await store.IsAllowedAsync("c", "orders", "dev", default));
        Assert.False(await store.IsAllowedAsync("c", "orders", "dev", default));
        Assert.Equal(1, source.GrantReads);

        source.Grants.Add(new AccessGrant("c", "orders", "*"));
        await store.ReloadAsync(default);

        Assert.True(await store.IsAllowedAsync("c", "orders", "prod", default));
    }

    [Fact]
    public async Task Dispatcher_routes_notifications()
    {
        var source = new FakeSource();
        source.Put("orders", "dev", 1);
        var snapshots = new SnapshotStore(source, new SnapshotBroadcaster(), NullLogger<SnapshotStore>.Instance);
        var grants = new AccessGrantStore(source);
        var dispatcher = new NotificationDispatcher(snapshots, grants, NullLogger<NotificationDispatcher>.Instance);
        await snapshots.GetAsync("orders", "dev", default);
        await grants.IsAllowedAsync("c", "orders", "dev", default);

        source.Put("orders", "dev", 2);
        source.Grants.Add(new AccessGrant("c", "orders", "dev"));
        await dispatcher.DispatchAsync("config_published", "orders/dev/2", default);
        await dispatcher.DispatchAsync("access_changed", "", default);
        await dispatcher.DispatchAsync("config_published", "garbage", default);

        Assert.Equal(2, (await snapshots.GetAsync("orders", "dev", default))!.Version);
        Assert.True(await grants.IsAllowedAsync("c", "orders", "dev", default));
    }
}

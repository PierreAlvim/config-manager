using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ConfigManager.Distribution.Api.Caching;

/// <summary>A cached snapshot: deserialized form for evaluation, raw JSON for serving as-is.</summary>
public sealed record StoredSnapshot(ConfigSnapshot Snapshot, string Json)
{
    public long Version => Snapshot.Version;
}

/// <summary>In-process fan-out of new snapshot versions to SSE subscribers.</summary>
public sealed class SnapshotBroadcaster
{
    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    public int SubscriberCount => _subscriptions.Count;

    public Subscription Subscribe(string ns, string env)
    {
        var subscription = new Subscription(ns, env, s => _subscriptions.TryRemove(s, out _));
        _subscriptions[subscription] = 0;
        return subscription;
    }

    public void Publish(string ns, string env, StoredSnapshot snapshot)
    {
        foreach (var subscription in _subscriptions.Keys)
            if (subscription.Namespace == ns && subscription.Environment == env)
                subscription.Offer(snapshot);
    }
}

/// <summary>Holds only the latest undelivered snapshot; older ones are dropped.</summary>
public sealed class Subscription : IDisposable
{
    private readonly Channel<StoredSnapshot> _channel = Channel.CreateBounded<StoredSnapshot>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Action<Subscription> _onDispose;

    internal Subscription(string ns, string env, Action<Subscription> onDispose)
    {
        Namespace = ns;
        Environment = env;
        _onDispose = onDispose;
    }

    public string Namespace { get; }
    public string Environment { get; }
    public ChannelReader<StoredSnapshot> Reader => _channel.Reader;

    internal void Offer(StoredSnapshot snapshot) => _channel.Writer.TryWrite(snapshot);

    public void Dispose()
    {
        _onDispose(this);
        _channel.Writer.TryComplete();
    }
}

using System.Net;
using System.Text;
using ConfigManager.Contracts;
using OpenFeature.Constant;
using OpenFeature.Error;
using OpenFeature.Model;

namespace ConfigManager.OpenFeature.Tests;

public class ProviderSyncTests
{
    private sealed class Server
    {
        public long Version = 1;
        public bool Down;
        public string Banner = "none";
        public bool StreamFails;
        public string[] StreamBodies = [];

        public FakeHandler Handler { get; }

        public Server() => Handler = new FakeHandler(Respond);

        private HttpResponseMessage Respond(HttpRequestMessage r)
        {
            var url = r.RequestUri!.ToString();
            if (url == T.TokenUrl) return Down ? new(HttpStatusCode.ServiceUnavailable) : T.Token();
            if (Down) return new(HttpStatusCode.ServiceUnavailable);
            if (url.EndsWith("/stream"))
            {
                if (StreamFails) return new(HttpStatusCode.InternalServerError);
                var sb = new StringBuilder();
                foreach (var b in StreamBodies) sb.Append($"event: snapshot\nid: x\ndata: {b.ReplaceLineEndings("")}\n\n");
                return T.Sse(sb.ToString());
            }

            var etag = $"\"{Version}\"";
            if (r.Headers.IfNoneMatch.Any(m => m.Tag == etag)) return new(HttpStatusCode.NotModified);
            return T.Json(T.SnapshotJson(Version, bannerDefault: Banner), etag: etag);
        }
    }

    private static ConfigManagerProvider Create(Server server, Action<ConfigManagerProviderOptions>? configure = null)
    {
        var p = ConfigManagerProvider.Create(T.Options(configure), new HttpClient(server.Handler));
        p.InitialBackoff = TimeSpan.FromMilliseconds(10);
        p.MaxBackoff = TimeSpan.FromMilliseconds(40);
        return p;
    }

    private static async Task<ProviderEventPayload> NextEvent(ConfigManagerProvider p, int ms = 5000)
    {
        using var cts = new CancellationTokenSource(ms);
        return (ProviderEventPayload)await p.EventReader.ReadAsync(cts.Token);
    }

    [Fact]
    public async Task Initialize_loads_snapshot()
    {
        var server = new Server { Version = 4 };
        var p = Create(server);
        await p.InitializeAsync(EvaluationContext.Empty);
        Assert.Equal(4, p.CurrentSnapshot!.Version);
        await p.ShutdownAsync();
    }

    [Fact]
    public async Task Polling_uses_etag_and_emits_changed_keys_on_new_version()
    {
        var server = new Server();
        var p = Create(server);
        await p.InitializeAsync(EvaluationContext.Empty);

        await T.Eventually(() => server.Handler.Requests.Any(r => r.Headers.ContainsKey("If-None-Match")));
        Assert.Equal(1, p.CurrentSnapshot!.Version);

        server.Banner = "Hello";
        server.Version = 2;
        var evt = await NextEvent(p);

        Assert.Equal(ProviderEventTypes.ProviderConfigurationChanged, evt.Type);
        Assert.Equal(["str"], evt.FlagsChanged);
        Assert.Equal(2, p.CurrentSnapshot!.Version);
        Assert.Equal("Hello", (await p.ResolveStringValueAsync("str", "")).Value);
        await p.ShutdownAsync();
    }

    [Fact]
    public async Task Streaming_applies_pushed_versions_and_reports_added_and_removed_keys()
    {
        var server = new Server();
        var snap2 = SnapshotJson.Deserialize(T.SnapshotJson(2));
        snap2.Flags.Remove("bool-static");
        snap2.Flags["extra"] = SnapshotJson.DeserializeFlag("""{"type":"boolean","variants":{"a":true},"defaultVariant":"a"}""");
        server.StreamBodies = [SnapshotJson.Serialize(snap2)];

        var p = Create(server, o => o.Mode = ConfigManagerMode.Streaming);
        await p.InitializeAsync(EvaluationContext.Empty);

        var evt = await NextEvent(p);
        Assert.Equal(ProviderEventTypes.ProviderConfigurationChanged, evt.Type);
        Assert.Equal(["bool-static", "extra"], evt.FlagsChanged);
        Assert.Equal(2, p.CurrentSnapshot!.Version);
        await p.ShutdownAsync();
    }

    [Fact]
    public async Task Streaming_falls_back_to_etag_polling_when_stream_keeps_failing()
    {
        var server = new Server { StreamFails = true };
        var p = Create(server, o => o.Mode = ConfigManagerMode.Streaming);
        await p.InitializeAsync(EvaluationContext.Empty);

        server.Version = 2;
        var evt = await NextEvent(p);
        Assert.Equal(ProviderEventTypes.ProviderConfigurationChanged, evt.Type);
        Assert.Equal(2, p.CurrentSnapshot!.Version);
        Assert.True(server.Handler.Count("/stream") >= 3);
        await p.ShutdownAsync();
    }

    [Fact]
    public async Task Same_version_does_not_raise_event()
    {
        var server = new Server();
        server.StreamBodies = [T.SnapshotJson(1)];
        var p = Create(server, o => o.Mode = ConfigManagerMode.Streaming);
        await p.InitializeAsync(EvaluationContext.Empty);
        await T.Eventually(() => server.Handler.Count("/stream") >= 3);
        Assert.False(p.EventReader.TryRead(out _));
        await p.ShutdownAsync();
    }

    [Fact]
    public async Task Initialize_fails_without_server_and_cache_then_recovers_with_ready_event()
    {
        var server = new Server { Down = true };
        var p = Create(server);

        var ex = await Assert.ThrowsAsync<FeatureProviderException>(() => p.InitializeAsync(EvaluationContext.Empty));
        Assert.Null(p.CurrentSnapshot);
        Assert.Equal(ErrorType.ProviderNotReady, (await p.ResolveBooleanValueAsync("bool-static", false)).ErrorType);

        server.Down = false;
        var evt = await NextEvent(p);
        Assert.Equal(ProviderEventTypes.ProviderReady, evt.Type);
        Assert.NotNull(p.CurrentSnapshot);
        Assert.True((await p.ResolveBooleanValueAsync("bool-static", false)).Value);
        await p.ShutdownAsync();
    }

    [Fact]
    public async Task Cache_file_is_written_and_used_when_server_is_down()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cm-cache-{Guid.NewGuid():N}", "snapshot.json");
        try
        {
            var server = new Server { Version = 5 };
            var first = Create(server, o => o.CacheFilePath = path);
            await first.InitializeAsync(EvaluationContext.Empty);
            await first.ShutdownAsync();
            Assert.True(File.Exists(path));
            Assert.Equal(5, SnapshotJson.Deserialize(File.ReadAllText(path)).Version);

            var down = new Server { Down = true };
            var second = Create(down, o => o.CacheFilePath = path);
            await second.InitializeAsync(EvaluationContext.Empty); // does not throw
            Assert.Equal(5, second.CurrentSnapshot!.Version);
            Assert.True((await second.ResolveBooleanValueAsync("bool-static", false)).Value);

            // server comes back with a newer version: provider catches up and reports the change
            down.Down = false;
            down.Version = 6;
            var evt = await NextEvent(second);
            Assert.Equal(ProviderEventTypes.ProviderConfigurationChanged, evt.Type);
            Assert.Equal(6, second.CurrentSnapshot!.Version);
            await second.ShutdownAsync();
            Assert.Equal(6, SnapshotJson.Deserialize(File.ReadAllText(path)).Version);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public async Task Cache_for_other_namespace_is_ignored()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cm-cache-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, T.SnapshotJson(9, ns: "payments"));
        try
        {
            var p = Create(new Server { Down = true }, o => o.CacheFilePath = path);
            await Assert.ThrowsAsync<FeatureProviderException>(() => p.InitializeAsync(EvaluationContext.Empty));
            await p.ShutdownAsync();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Shutdown_stops_the_loop()
    {
        var server = new Server();
        var p = Create(server);
        await p.InitializeAsync(EvaluationContext.Empty);
        await p.ShutdownAsync();
        var count = server.Handler.Requests.Count;
        await Task.Delay(150);
        Assert.Equal(count, server.Handler.Requests.Count);
    }

    [Fact]
    public void Changed_keys_detects_modified_added_removed()
    {
        var a = SnapshotJson.Deserialize(T.SnapshotJson(1));
        var b = SnapshotJson.Deserialize(T.SnapshotJson(2, bannerDefault: "x"));
        b.Flags.Remove("int");
        Assert.Equal(["int", "str"], ConfigManagerProvider.ChangedKeys(a, b));
        Assert.Equal(a.Flags.Count, ConfigManagerProvider.ChangedKeys(null, a).Count);
    }

    [Fact]
    public void Options_validation_requires_core_values()
    {
        Assert.Throws<InvalidOperationException>(() => new ConfigManagerProviderOptions().Validate());
        T.Options().Validate();
    }
}

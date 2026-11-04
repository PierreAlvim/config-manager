using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace ConfigManager.OpenFeature.Tests;

/// <summary>Provider against an in-process fake Distribution API (real HTTP + SSE).</summary>
public class ProviderIntegrationTests
{
    [Fact]
    public async Task Provider_receives_pushed_version_over_sse()
    {
        await using var server = new FakeDistributionServer();
        await server.StartAsync();
        var provider = ConfigManagerProvider.Create(server.Options());

        await provider.InitializeAsync(EvaluationContext.Empty);
        Assert.Equal("none", (await provider.ResolveStringValueAsync("str", "")).Value);
        Assert.Equal("Bearer integration-token", server.LastAuthorization);
        await T.Eventually(() => server.StreamConnections == 1);

        server.Publish(T.SnapshotJson(2, bannerDefault: "Black Friday"));

        using var cts = new CancellationTokenSource(5000);
        var evt = (ProviderEventPayload)await provider.EventReader.ReadAsync(cts.Token);
        Assert.Equal(ProviderEventTypes.ProviderConfigurationChanged, evt.Type);
        Assert.Equal(["str"], evt.FlagsChanged);

        var r = await provider.ResolveStringValueAsync("str", "");
        Assert.Equal("Black Friday", r.Value);
        Assert.Equal(2, r.FlagMetadata!.GetInt("version"));
        Assert.Equal(1, server.TokenRequests); // token cached across snapshot + stream requests
        await provider.ShutdownAsync();
    }

    [Fact]
    public async Task Polling_mode_picks_up_new_version_via_etag()
    {
        await using var server = new FakeDistributionServer();
        await server.StartAsync();
        var provider = ConfigManagerProvider.Create(server.Options(o => o.Mode = ConfigManagerMode.Polling));
        await provider.InitializeAsync(EvaluationContext.Empty);
        server.Publish(T.SnapshotJson(2, bannerDefault: "polled"));
        await T.Eventually(() => provider.CurrentSnapshot!.Version == 2); // found through ETag polling
        await provider.ShutdownAsync();
    }
}

/// <summary>Uses the process-wide <see cref="Api.Instance"/>, so it must not run in parallel with other users of it.</summary>
[CollectionDefinition("OpenFeature Api", DisableParallelization = true)]
public class OpenFeatureApiCollection;

[Collection("OpenFeature Api")]
public class DependencyInjectionIntegrationTests
{
    [Fact]
    public async Task Hosted_provider_serves_client_and_reports_changes()
    {
        await using var server = new FakeDistributionServer();
        await server.StartAsync();
        var options = server.Options();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddConfigManagerOpenFeature(o =>
        {
            o.DistributionUrl = options.DistributionUrl;
            o.Namespace = options.Namespace;
            o.Environment = options.Environment;
            o.TokenEndpoint = options.TokenEndpoint;
            o.ClientId = options.ClientId;
            o.ClientSecret = options.ClientSecret;
        });
        using var host = builder.Build();

        var changed = new TaskCompletionSource<ProviderEventPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        Api.Instance.AddHandler(ProviderEventTypes.ProviderConfigurationChanged, e => changed.TrySetResult(e!));
        try
        {
            await host.StartAsync();
            var client = host.Services.GetRequiredService<IFeatureClient>();
            Assert.Equal(ProviderStatus.Ready, client.ProviderStatus);

            var ctx = T.Ctx("u1", ("tenant", new Value("acme")));
            Assert.True(await client.GetBooleanValueAsync("bool-rule", false, ctx));
            var details = await client.GetStringDetailsAsync("str", "x");
            Assert.Equal("none", details.Value);
            Assert.Equal(Reason.Static, details.Reason);

            server.Publish(T.SnapshotJson(2, bannerDefault: "new"));

            var payload = await T.WithTimeout(changed.Task);
            Assert.Contains("str", payload.FlagsChanged!);
            Assert.Equal("new", await client.GetStringValueAsync("str", "x"));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task Provider_recovers_when_distribution_api_comes_up_later()
    {
        await using var server = new FakeDistributionServer { Down = true };
        await server.StartAsync();
        var good = server.Options();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddConfigManagerOpenFeature(o =>
        {
            o.DistributionUrl = good.DistributionUrl;
            o.Namespace = good.Namespace;
            o.Environment = good.Environment;
            o.TokenEndpoint = good.TokenEndpoint;
            o.ClientId = good.ClientId;
            o.ClientSecret = good.ClientSecret;
            o.InitializationTimeout = TimeSpan.FromSeconds(2);
        });
        using var host = builder.Build();
        var provider = host.Services.GetRequiredService<ConfigManagerProvider>();
        provider.InitialBackoff = TimeSpan.FromMilliseconds(50);
        provider.MaxBackoff = TimeSpan.FromMilliseconds(200);
        await host.StartAsync(); // must neither throw nor hang while the API is unavailable
        try
        {
            var client = host.Services.GetRequiredService<IFeatureClient>();
            Assert.NotEqual(ProviderStatus.Ready, client.ProviderStatus);
            Assert.False(await client.GetBooleanValueAsync("bool-static", false));

            server.Down = false;
            await T.Eventually(() => client.ProviderStatus == ProviderStatus.Ready);
            Assert.True(await client.GetBooleanValueAsync("bool-static", false));
        }
        finally
        {
            await host.StopAsync();
        }
    }
}

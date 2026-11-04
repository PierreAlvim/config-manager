using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenFeature;

namespace ConfigManager.OpenFeature;

public static class ServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="IFeatureClient"/> registered by <c>AddConfigManagerOpenFeature</c>.</summary>
    public const string ClientName = "config-manager";

    /// <summary>Registers the provider using options bound from <paramref name="section"/> (usually <c>ConfigManager</c>).</summary>
    public static IServiceCollection AddConfigManagerOpenFeature(this IServiceCollection services, IConfiguration section)
    {
        services.AddOptions<ConfigManagerProviderOptions>().Bind(section);
        return services.AddCore();
    }

    /// <summary>Registers the provider using programmatic options.</summary>
    public static IServiceCollection AddConfigManagerOpenFeature(this IServiceCollection services, Action<ConfigManagerProviderOptions> configure)
    {
        services.AddOptions<ConfigManagerProviderOptions>().Configure(configure);
        return services.AddCore();
    }

    private static IServiceCollection AddCore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<ConfigManagerProviderOptions>>().Value);

        // Streaming requests are long-lived: no overall HttpClient timeout, SnapshotClient applies its own.
        services.AddHttpClient<ClientCredentialsTokenProvider>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<SnapshotClient>(c => c.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

        services.TryAddSingleton<ConfigManagerProvider>(sp => new ConfigManagerProvider(
            sp.GetRequiredService<ConfigManagerProviderOptions>(),
            sp.GetRequiredService<SnapshotClient>(),
            sp.GetService<ILogger<ConfigManagerProvider>>()));
        services.TryAddSingleton<IFeatureClient>(_ => Api.Instance.GetClient(ClientName));
        services.AddHostedService<ConfigManagerHostedService>();
        return services;
    }
}

/// <summary>Registers the provider with <see cref="Api.Instance"/> at startup and shuts OpenFeature down on stop.</summary>
internal sealed class ConfigManagerHostedService(ConfigManagerProvider provider, ILogger<ConfigManagerHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Bounded by InitializationTimeout. On failure the provider keeps retrying in the background and
            // flips to READY (ProviderReady event) as soon as the Distribution API answers.
            await Api.Instance.SetProviderAsync(provider, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Config Manager provider registered (snapshot loaded: {Loaded})", provider.CurrentSnapshot is not null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Config Manager provider failed to initialize; continuing and retrying in the background");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Api.Instance.ShutdownAsync();
}

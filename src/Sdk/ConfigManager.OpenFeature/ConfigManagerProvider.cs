using System.Text.Json;
using ConfigManager.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Error;
using OpenFeature.Model;

namespace ConfigManager.OpenFeature;

/// <summary>
/// OpenFeature provider evaluating flags locally against a snapshot synchronised from the Config Manager
/// Distribution API (SSE streaming with ETag-polling fallback).
/// </summary>
public sealed class ConfigManagerProvider : FeatureProvider
{
    public const string ProviderName = "ConfigManager";

    private delegate bool TryConvert<T>(JsonElement element, out T value);

    private readonly ConfigManagerProviderOptions _options;
    private readonly SnapshotClient _client;
    private readonly ILogger _logger;
    private readonly object _applyLock = new();

    private ConfigSnapshot? _snapshot;
    private string? _etag;
    private bool _initializationFailed;
    private CancellationTokenSource? _loopCts;
    private Task _loop = Task.CompletedTask;

    public ConfigManagerProvider(ConfigManagerProviderOptions options, SnapshotClient client, ILogger<ConfigManagerProvider>? logger = null)
    {
        options.Validate();
        _options = options;
        _client = client;
        _logger = logger ?? NullLogger<ConfigManagerProvider>.Instance;
    }

    /// <summary>Convenience factory for non-DI usage: one HttpClient is used for both token and snapshot requests.</summary>
    public static ConfigManagerProvider Create(ConfigManagerProviderOptions options, HttpClient? httpClient = null, ILoggerFactory? loggerFactory = null)
    {
        httpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var tokens = new ClientCredentialsTokenProvider(httpClient, options, logger: loggerFactory?.CreateLogger<ClientCredentialsTokenProvider>());
        var client = new SnapshotClient(httpClient, tokens, options, loggerFactory?.CreateLogger<SnapshotClient>());
        return new ConfigManagerProvider(options, client, loggerFactory?.CreateLogger<ConfigManagerProvider>());
    }

    /// <summary>First reconnect delay of the sync loop (doubles up to <see cref="MaxBackoff"/>).</summary>
    internal TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    internal TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Number of consecutive SSE failures after which every retry round also polls with ETag.</summary>
    internal int PollFallbackAfterFailures { get; set; } = 3;

    internal System.Threading.Channels.ChannelReader<object> EventReader => EventChannel.Reader;

    /// <summary>The snapshot currently used for evaluation (null until loaded).</summary>
    public ConfigSnapshot? CurrentSnapshot => Volatile.Read(ref _snapshot);

    public override Metadata GetMetadata() => new(ProviderName);

    // ---- lifecycle -------------------------------------------------------------------------------------------

    public override async Task InitializeAsync(EvaluationContext context, CancellationToken cancellationToken = default)
    {
        var loopCts = new CancellationTokenSource();
        _loopCts = loopCts;
        Exception? failure = null;

        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(_options.InitializationTimeout);
            try
            {
                var result = await _client.GetSnapshotAsync(null, timeout.Token).ConfigureAwait(false);
                if (result.Snapshot is not null) Apply(result.Snapshot, result.ETag, notify: false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failure = ex;
            }
        }

        if (failure is not null)
        {
            _logger.LogWarning(failure, "Could not load initial snapshot for {Namespace}/{Environment}", _options.Namespace, _options.Environment);
            var fromCache = TryLoadCache();
            if (fromCache is not null)
            {
                Apply(fromCache, null, notify: false);
                _logger.LogWarning("Using cached snapshot version {Version} from {Path}", fromCache.Version, _options.CacheFilePath);
                failure = null;
            }
        }

        if (failure is not null)
        {
            lock (_applyLock)
            {
                // The loop may not have started yet, but be defensive: a snapshot that raced in means we are ready.
                if (CurrentSnapshot is null) _initializationFailed = true;
                else failure = null;
            }
        }

        // The loop always runs so the provider recovers once the Distribution API becomes reachable.
        _loop = Task.Run(() => RunLoopAsync(loopCts.Token), CancellationToken.None);

        if (failure is not null)
        {
            throw new FeatureProviderException(ErrorType.ProviderNotReady,
                $"Could not load snapshot for {_options.Namespace}/{_options.Environment}: {failure.Message}", failure);
        }
    }

    public override async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var cts = Interlocked.Exchange(ref _loopCts, null);
        if (cts is null) return;

        await cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await _loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        cts.Dispose();
    }

    // ---- synchronisation -------------------------------------------------------------------------------------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var backoff = InitialBackoff;
        var sseFailures = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_options.Mode == ConfigManagerMode.Streaming)
                {
                    var received = 0;
                    try
                    {
                        await _client.StreamAsync(s =>
                        {
                            received++;
                            Apply(s, null, notify: true);
                            return Task.CompletedTask;
                        }, ct).ConfigureAwait(false);
                        _logger.LogInformation("Snapshot stream closed by server, reconnecting");
                    }
                    finally
                    {
                        if (received > 0)
                        {
                            sseFailures = 0;
                            backoff = InitialBackoff;
                        }
                    }

                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
                else
                {
                    await PollOnceAsync(ct).ConfigureAwait(false);
                    backoff = InitialBackoff;
                    await Task.Delay(_options.PollInterval, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Snapshot synchronisation failed; retrying in {Delay}", backoff);

                if (_options.Mode == ConfigManagerMode.Streaming && ++sseFailures >= PollFallbackAfterFailures)
                {
                    try
                    {
                        await PollOnceAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception pollEx)
                    {
                        _logger.LogWarning(pollEx, "Fallback poll failed");
                    }
                }

                try
                {
                    await Task.Delay(backoff, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var result = await _client.GetSnapshotAsync(Volatile.Read(ref _etag), ct).ConfigureAwait(false);
        if (result.Snapshot is not null) Apply(result.Snapshot, result.ETag, notify: true);
    }

    /// <summary>Atomically swaps the snapshot when its version differs and raises the matching provider event.</summary>
    private void Apply(ConfigSnapshot next, string? etag, bool notify)
    {
        if (!string.Equals(next.Namespace, _options.Namespace, StringComparison.Ordinal) ||
            !string.Equals(next.Environment, _options.Environment, StringComparison.Ordinal))
        {
            _logger.LogWarning("Ignoring snapshot for {Namespace}/{Environment}; expected {ExpectedNamespace}/{ExpectedEnvironment}",
                next.Namespace, next.Environment, _options.Namespace, _options.Environment);
            return;
        }

        ConfigSnapshot? previous;
        bool recovered;
        lock (_applyLock)
        {
            previous = _snapshot;
            if (previous is not null && previous.Version == next.Version)
            {
                _etag = etag ?? _etag;
                return;
            }

            Volatile.Write(ref _snapshot, next);
            _etag = etag ?? $"\"{next.Version}\"";
            recovered = _initializationFailed;
            _initializationFailed = false;
        }

        _logger.LogInformation("Applied snapshot {Namespace}/{Environment} version {Version} ({Count} flags)",
            next.Namespace, next.Environment, next.Version, next.Flags.Count);
        PersistCache(next);

        if (!notify) return;

        if (recovered)
        {
            Emit(new ProviderEventPayload { Type = ProviderEventTypes.ProviderReady, Message = "Snapshot loaded" });
        }
        else
        {
            Emit(new ProviderEventPayload
            {
                Type = ProviderEventTypes.ProviderConfigurationChanged,
                Message = $"Snapshot version {next.Version}",
                FlagsChanged = ChangedKeys(previous, next),
            });
        }
    }

    private void Emit(ProviderEventPayload payload)
    {
        payload.ProviderName = ProviderName;
        if (!EventChannel.Writer.TryWrite(payload))
            _logger.LogDebug("Could not publish provider event {Type}", payload.Type);
    }

    internal static List<string> ChangedKeys(ConfigSnapshot? previous, ConfigSnapshot next)
    {
        var changed = new List<string>();
        foreach (var (key, flag) in next.Flags)
        {
            if (previous is null || !previous.Flags.TryGetValue(key, out var old) ||
                SnapshotJson.Serialize(old) != SnapshotJson.Serialize(flag))
                changed.Add(key);
        }

        if (previous is not null)
            changed.AddRange(previous.Flags.Keys.Where(k => !next.Flags.ContainsKey(k)));

        changed.Sort(StringComparer.Ordinal);
        return changed;
    }

    // ---- last-known-good cache -------------------------------------------------------------------------------

    private ConfigSnapshot? TryLoadCache()
    {
        var path = _options.CacheFilePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            var snapshot = SnapshotJson.Deserialize(File.ReadAllText(path));
            if (snapshot.Namespace == _options.Namespace && snapshot.Environment == _options.Environment) return snapshot;
            _logger.LogWarning("Cache file {Path} belongs to {Namespace}/{Environment}; ignoring", path, snapshot.Namespace, snapshot.Environment);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read cache file {Path}", path);
        }

        return null;
    }

    private void PersistCache(ConfigSnapshot snapshot)
    {
        var path = _options.CacheFilePath;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var temp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(temp, SnapshotJson.Serialize(snapshot));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not write cache file {Path}", path);
        }
    }

    // ---- evaluation ------------------------------------------------------------------------------------------

    public override Task<ResolutionDetails<bool>> ResolveBooleanValueAsync(string flagKey, bool defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, EvaluationMapper.TryBoolean));

    public override Task<ResolutionDetails<string>> ResolveStringValueAsync(string flagKey, string defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, EvaluationMapper.TryString));

    public override Task<ResolutionDetails<int>> ResolveIntegerValueAsync(string flagKey, int defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, EvaluationMapper.TryInteger));

    public override Task<ResolutionDetails<double>> ResolveDoubleValueAsync(string flagKey, double defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, EvaluationMapper.TryDouble));

    public override Task<ResolutionDetails<Value>> ResolveStructureValueAsync(string flagKey, Value defaultValue, EvaluationContext? context = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context, EvaluationMapper.TryStructure));

    private ResolutionDetails<T> Resolve<T>(string flagKey, T defaultValue, EvaluationContext? context, TryConvert<T> convert)
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is null)
        {
            return new ResolutionDetails<T>(flagKey, defaultValue, ErrorType.ProviderNotReady, Reason.Error,
                errorMessage: "Provider has no snapshot loaded yet");
        }

        var metadata = CreateMetadata(snapshot);
        var result = FlagEvaluator.Evaluate(snapshot, flagKey, EvaluationMapper.ToInput(context));

        if (result.Reason == EvaluationReason.Disabled)
            return new ResolutionDetails<T>(flagKey, defaultValue, ErrorType.None, Reason.Disabled, flagMetadata: metadata);

        if (result.ErrorCode != EvaluationErrorCode.None || result.Value is not { } json)
        {
            var code = result.ErrorCode == EvaluationErrorCode.None ? EvaluationErrorCode.General : result.ErrorCode;
            return new ResolutionDetails<T>(flagKey, defaultValue, EvaluationMapper.ToErrorType(code), Reason.Error,
                errorMessage: result.ErrorMessage ?? "Flag has no value", flagMetadata: metadata);
        }

        if (!convert(json, out var value))
        {
            return new ResolutionDetails<T>(flagKey, defaultValue, ErrorType.TypeMismatch, Reason.Error, result.Variant,
                $"Flag '{flagKey}' value of kind {json.ValueKind} does not match the requested type {typeof(T).Name}", metadata);
        }

        return new ResolutionDetails<T>(flagKey, value, ErrorType.None, EvaluationMapper.ToReason(result.Reason), result.Variant, flagMetadata: metadata);
    }

    private static ImmutableMetadata CreateMetadata(ConfigSnapshot snapshot) => new(new Dictionary<string, object>
    {
        ["version"] = (int)Math.Min(snapshot.Version, int.MaxValue),
        ["namespace"] = snapshot.Namespace,
        ["environment"] = snapshot.Environment,
    });
}

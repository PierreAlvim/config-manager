# ConfigManager.OpenFeature

OpenFeature .NET provider for Config Manager. Flags are evaluated **locally** against a snapshot that is kept
in sync with the Distribution API (SSE stream, ETag polling fallback). Authentication uses the OIDC
client-credentials grant.

```csharp
builder.Services.AddConfigManagerOpenFeature(builder.Configuration.GetSection("ConfigManager"));

app.MapGet("/", async (IFeatureClient flags) =>
    await flags.GetBooleanValueAsync("new-checkout", false,
        EvaluationContext.Builder().SetTargetingKey("u1").Set("tenant", "acme").Build()));
```

```json
"ConfigManager": {
  "DistributionUrl": "http://localhost:5002",
  "Namespace": "orders",
  "Environment": "dev",
  "TokenEndpoint": "http://localhost:8080/realms/config-manager/protocol/openid-connect/token",
  "ClientId": "orders-service",
  "ClientSecret": "orders-service-secret",
  "Mode": "Streaming",            // or "Polling"
  "PollInterval": "00:00:30",
  "CacheFilePath": "/var/cache/orders-flags.json",   // optional last-known-good file
  "InitializationTimeout": "00:00:10"
}
```

* If the first load fails the provider uses `CacheFilePath` when available; otherwise it starts in `ERROR`, keeps
  retrying with exponential backoff (1s to 30s) and becomes `READY` when the API is reachable.
* On every new version `ProviderConfigurationChanged` is raised with the changed flag keys
  (`Api.Instance.AddHandler(ProviderEventTypes.ProviderConfigurationChanged, ...)`).
* Evaluation details carry flag metadata `version`, `namespace`, `environment`.
* Without DI: `ConfigManagerProvider.Create(options)` then `await Api.Instance.SetProviderAsync(provider)`.

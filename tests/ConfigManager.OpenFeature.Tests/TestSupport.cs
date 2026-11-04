using System.Net;
using System.Text;
using ConfigManager.Contracts;
using OpenFeature.Model;

namespace ConfigManager.OpenFeature.Tests;

internal sealed class FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    private readonly object _lock = new();
    public List<(HttpMethod Method, string Url, Dictionary<string, string> Headers, string? Body)> Requests { get; } = [];

    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this(r => Task.FromResult(respond(r)))
    {
    }

    public int Count(string pathContains) { lock (_lock) return Requests.Count(r => r.Url.Contains(pathContains)); }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        lock (_lock) Requests.Add((request.Method, request.RequestUri!.ToString(), headers, body));
        return await respond(request);
    }
}

internal static class T
{
    public const string TokenUrl = "http://idp.test/token";

    public static ConfigManagerProviderOptions Options(Action<ConfigManagerProviderOptions>? configure = null)
    {
        var o = new ConfigManagerProviderOptions
        {
            DistributionUrl = "http://dist.test",
            Namespace = "orders",
            Environment = "dev",
            TokenEndpoint = TokenUrl,
            ClientId = "orders-service",
            ClientSecret = "secret",
            Mode = ConfigManagerMode.Polling,
            PollInterval = TimeSpan.FromMilliseconds(30),
            InitializationTimeout = TimeSpan.FromSeconds(5),
        };
        configure?.Invoke(o);
        return o;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK, string? etag = null)
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (etag is not null) r.Headers.TryAddWithoutValidation("ETag", etag);
        return r;
    }

    public static HttpResponseMessage Token(string token = "tok", int expiresIn = 300) =>
        Json($$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":{{expiresIn}}}""");

    public static HttpResponseMessage Sse(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "text/event-stream") };

    /// <summary>Snapshot with flags covering every type and rule kind.</summary>
    public static string SnapshotJson(long version, string ns = "orders", string env = "dev", string bannerDefault = "none") => $$"""
    {
      "namespace": "{{ns}}", "environment": "{{env}}", "version": {{version}}, "publishedAt": "2026-11-04T10:00:00Z",
      "flags": {
        "bool-static": { "type": "boolean", "enabled": true, "variants": { "on": true, "off": false }, "defaultVariant": "on" },
        "bool-rule": { "type": "boolean", "enabled": true, "variants": { "on": true, "off": false }, "defaultVariant": "off",
          "rules": [ { "name": "acme", "conditions": [ { "attribute": "tenant", "operator": "in", "values": ["acme"] } ], "variant": "on" } ] },
        "bool-split": { "type": "boolean", "enabled": true, "variants": { "on": true, "off": false }, "defaultVariant": "off",
          "rules": [ { "conditions": [], "rollout": [ { "variant": "on", "weight": 100 } ] } ] },
        "num-attr": { "type": "boolean", "enabled": true, "variants": { "on": true, "off": false }, "defaultVariant": "off",
          "rules": [ { "conditions": [ { "attribute": "age", "operator": "greaterThanOrEquals", "values": ["18"] } ], "variant": "on" } ] },
        "str": { "type": "string", "enabled": true, "variants": { "none": "{{bannerDefault}}", "bf": "Black Friday" }, "defaultVariant": "none" },
        "int": { "type": "integer", "enabled": true, "variants": { "small": 10, "large": 50 }, "defaultVariant": "small" },
        "dbl": { "type": "double", "enabled": true, "variants": { "std": 2.5, "whole": 3 }, "defaultVariant": "std" },
        "obj": { "type": "object", "enabled": true, "variants": { "v": { "primary": "stripe", "n": 2, "ratio": 0.5, "ok": true, "none": null, "list": [1, "a", { "x": 1 }] } }, "defaultVariant": "v" },
        "arr": { "type": "object", "enabled": true, "variants": { "v": [1, 2] }, "defaultVariant": "v" },
        "off": { "type": "boolean", "enabled": false, "variants": { "on": true }, "defaultVariant": "on" }
      }
    }
    """;

    public static async Task<T1> WithTimeout<T1>(Task<T1> task, int ms = 5000)
    {
        var done = await Task.WhenAny(task, Task.Delay(ms));
        if (done != task) throw new TimeoutException("Operation timed out");
        return await task;
    }

    public static async Task Eventually(Func<bool> condition, int ms = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met in time");
            await Task.Delay(10);
        }
    }

    public static EvaluationContext Ctx(string? key = null, params (string, Value)[] attrs)
    {
        var b = EvaluationContext.Builder();
        if (key is not null) b.SetTargetingKey(key);
        foreach (var (k, v) in attrs) b.Set(k, v);
        return b.Build();
    }
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace ConfigManager.Distribution.Tests.Integration;

public class OfrepApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Base = "/ns/orders/env/dev/ofrep/v1/evaluate/flags";

    private async Task<(DistributionFactory Factory, HttpClient Client)> StartAsync()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 5), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        var factory = new DistributionFactory(db.ConnectionString);
        return (factory, factory.CreateClientFor("svc"));
    }

    private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Targeting_match_uses_context()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var response = await client.PostAsync($"{Base}/new-checkout", Body("""{ "context": { "targetingKey": "u1", "tenant": "acme" } }"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadAsync(response);
        Assert.Equal("new-checkout", json.GetProperty("key").GetString());
        Assert.True(json.GetProperty("value").GetBoolean());
        Assert.Equal("on", json.GetProperty("variant").GetString());
        Assert.Equal("TARGETING_MATCH", json.GetProperty("reason").GetString());
        Assert.Equal(5, json.GetProperty("metadata").GetProperty("version").GetInt64());
        Assert.Equal("checkout-team", json.GetProperty("metadata").GetProperty("owner").GetString());
    }

    [Fact]
    public async Task Split_is_deterministic_and_matches_the_evaluator()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;
        var snapshot = TestData.Snapshot("orders", "dev", 5);
        var flag = snapshot.Flags["new-checkout"];

        foreach (var user in new[] { "alice", "bob", "carol", "dave", "erin", "frank" })
        {
            var expected = FlagEvaluator.Evaluate(flag, "new-checkout",
                new EvaluationInput(user, new Dictionary<string, string?>()));
            var body = $$"""{ "context": { "targetingKey": "{{user}}" } }""";

            var first = await ReadAsync(await client.PostAsync($"{Base}/new-checkout", Body(body)));
            var second = await ReadAsync(await client.PostAsync($"{Base}/new-checkout", Body(body)));

            Assert.Equal("SPLIT", first.GetProperty("reason").GetString());
            Assert.Equal(expected.Variant, first.GetProperty("variant").GetString());
            Assert.Equal(first.GetProperty("variant").GetString(), second.GetProperty("variant").GetString());
        }
    }

    [Fact]
    public async Task Without_targeting_key_rollout_is_skipped_and_default_applies()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var json = await ReadAsync(await client.PostAsync($"{Base}/new-checkout", Body("{}")));

        Assert.Equal("DEFAULT", json.GetProperty("reason").GetString());
        Assert.Equal("off", json.GetProperty("variant").GetString());
    }

    [Fact]
    public async Task Static_flag_without_rules()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var json = await ReadAsync(await client.PostAsync($"{Base}/max-items", content: null));

        Assert.Equal("STATIC", json.GetProperty("reason").GetString());
        Assert.Equal(10, json.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task Unknown_flag_is_404_flag_not_found()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var response = await client.PostAsync($"{Base}/nope", Body("{}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var json = await ReadAsync(response);
        Assert.Equal("FLAG_NOT_FOUND", json.GetProperty("errorCode").GetString());
        Assert.Equal("nope", json.GetProperty("key").GetString());
        Assert.True(json.TryGetProperty("errorDetails", out _));
    }

    [Fact]
    public async Task Disabled_flag_returns_null_value()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var response = await client.PostAsync($"{Base}/legacy-flow", Body("{}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadAsync(response);
        Assert.Equal("DISABLED", json.GetProperty("reason").GetString());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("value").ValueKind);
    }

    [Fact]
    public async Task Invalid_json_is_400_parse_error()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var response = await client.PostAsync($"{Base}/new-checkout", Body("{ nope"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("PARSE_ERROR", (await ReadAsync(response)).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task Non_string_context_values_are_normalized()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;
        // 'tenant' sent as number 7 must compare against "7" only; here it must NOT match acme/globex.
        var json = await ReadAsync(await client.PostAsync($"{Base}/new-checkout", Body("""{ "context": { "tenant": 7 } }""")));

        Assert.Equal("DEFAULT", json.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Bulk_returns_all_flags_with_etag_and_version()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;
        const string body = """{ "context": { "targetingKey": "u1", "tenant": "acme" } }""";

        var response = await client.PostAsync(Base, Body(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        var json = await ReadAsync(response);
        Assert.Equal(5, json.GetProperty("metadata").GetProperty("version").GetInt64());
        var flags = json.GetProperty("flags").EnumerateArray().ToDictionary(f => f.GetProperty("key").GetString()!);
        Assert.Equal(["checkout-banner", "legacy-flow", "max-items", "new-checkout"], flags.Keys.Order());
        Assert.Equal("TARGETING_MATCH", flags["new-checkout"].GetProperty("reason").GetString());
        Assert.Equal("DISABLED", flags["legacy-flow"].GetProperty("reason").GetString());
        Assert.Equal("Black Friday", flags["checkout-banner"].GetProperty("value").GetString());
    }

    [Fact]
    public async Task Bulk_supports_if_none_match_per_context()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;
        const string body = """{ "context": { "targetingKey": "u1" } }""";
        var etag = (await client.PostAsync(Base, Body(body))).Headers.ETag!.ToString();

        var same = new HttpRequestMessage(HttpMethod.Post, Base) { Content = Body(body) };
        same.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(same)).StatusCode);

        var otherContext = new HttpRequestMessage(HttpMethod.Post, Base) { Content = Body("""{ "context": { "targetingKey": "u2" } }""") };
        otherContext.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(otherContext)).StatusCode);
    }

    [Fact]
    public async Task Ofrep_requires_authentication_and_grant()
    {
        var (factory, _c) = await StartAsync();
        await using var disposable = factory;
        using var anonymous = factory.CreateClientFor(null);
        using var stranger = factory.CreateClientFor("stranger");

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(Base, Body("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync(Base, Body("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.PostAsync($"{Base}/new-checkout", Body("{}"))).StatusCode);
    }

    [Fact]
    public async Task Ofrep_grant_is_scoped_to_environment()
    {
        var (factory, client) = await StartAsync();
        await using var disposable = factory;

        var response = await client.PostAsync("/ns/orders/env/dev2/ofrep/v1/evaluate/flags", Body("{}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}

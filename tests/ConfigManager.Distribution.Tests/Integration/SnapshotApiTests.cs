using System.Net;
using System.Text.Json;

namespace ConfigManager.Distribution.Tests.Integration;

public class SnapshotApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private async Task<(TestDatabase Db, DistributionFactory Factory)> StartAsync(bool withSchema = true, TimeSpan? poll = null)
    {
        var db = await postgres.CreateDatabaseAsync(withSchema);
        return (db, new DistributionFactory(db.ConnectionString, poll));
    }

    [Fact]
    public async Task Returns_snapshot_with_etag_and_raw_json()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        var snapshot = TestData.Snapshot("orders", "dev", 3);
        await db.PublishAsync(snapshot, notify: false);
        await db.GrantAsync("orders-service", "orders", "dev", notify: false);
        using var client = factory.CreateClientFor("orders-service");

        var response = await client.GetAsync("/api/v1/snapshots/orders/dev");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("\"3\"", response.Headers.ETag!.Tag);
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        var parsed = SnapshotJson.Deserialize(await response.Content.ReadAsStringAsync());
        Assert.Equal(3, parsed.Version);
        Assert.Equal(snapshot.Flags.Keys.Order(), parsed.Flags.Keys.Order());
    }

    [Fact]
    public async Task If_none_match_gives_304()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 3), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        using var client = factory.CreateClientFor("svc");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/snapshots/orders/dev");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"3\"");
        var notModified = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal("\"3\"", notModified.Headers.ETag!.Tag);

        var stale = new HttpRequestMessage(HttpMethod.Get, "/api/v1/snapshots/orders/dev");
        stale.Headers.TryAddWithoutValidation("If-None-Match", "\"2\"");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(stale)).StatusCode);
    }

    [Fact]
    public async Task Latest_version_is_served()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 2), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        using var client = factory.CreateClientFor("svc");

        var response = await client.GetAsync("/api/v1/snapshots/orders/dev");

        Assert.Equal("\"2\"", response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Missing_or_unknown_token_gives_401()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        using var client = factory.CreateClientFor(null);

        var response = await client.GetAsync("/api/v1/snapshots/orders/dev");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task No_grant_gives_403_problem_details()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("someone-else", "orders", "dev", notify: false);
        using var client = factory.CreateClientFor("orders-service");

        var response = await client.GetAsync("/api/v1/snapshots/orders/dev");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Grant_for_other_environment_or_namespace_does_not_apply()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "prod", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        await db.GrantAsync("svc", "payments", "*", notify: false);
        using var client = factory.CreateClientFor("svc");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/snapshots/orders/prod")).StatusCode);
    }

    [Fact]
    public async Task Wildcard_environment_grant_covers_every_environment()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.PublishAsync(TestData.Snapshot("orders", "prod", 4), notify: false);
        await db.GrantAsync("svc", "orders", "*", notify: false);
        using var client = factory.CreateClientFor("svc");

        Assert.Equal("\"1\"", (await client.GetAsync("/api/v1/snapshots/orders/dev")).Headers.ETag!.Tag);
        Assert.Equal("\"4\"", (await client.GetAsync("/api/v1/snapshots/orders/prod")).Headers.ETag!.Tag);
    }

    [Fact]
    public async Task Unknown_pair_with_grant_gives_404()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.GrantAsync("svc", "orders", "*", notify: false);
        using var client = factory.CreateClientFor("svc");

        var response = await client.GetAsync("/api/v1/snapshots/orders/nope");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Published_notification_refreshes_cached_snapshot()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        using var client = factory.CreateClientFor("svc");
        Assert.Equal("\"1\"", (await client.GetAsync("/api/v1/snapshots/orders/dev")).Headers.ETag!.Tag);

        await db.PublishAsync(TestData.Snapshot("orders", "dev", 2));

        await Eventually.AssertAsync(async () =>
            Assert.Equal("\"2\"", (await client.GetAsync("/api/v1/snapshots/orders/dev")).Headers.ETag!.Tag));
    }

    [Fact]
    public async Task Safety_poll_picks_up_versions_without_notification()
    {
        var (db, factory) = await StartAsync(poll: TimeSpan.FromMilliseconds(300));
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        using var client = factory.CreateClientFor("svc");
        await client.GetAsync("/api/v1/snapshots/orders/dev");

        await db.PublishAsync(TestData.Snapshot("orders", "dev", 2), notify: false);

        await Eventually.AssertAsync(async () =>
            Assert.Equal("\"2\"", (await client.GetAsync("/api/v1/snapshots/orders/dev")).Headers.ETag!.Tag));
    }

    [Fact]
    public async Task Access_changes_take_effect_via_notification()
    {
        var (db, factory) = await StartAsync();
        await using var disposable = factory;
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        using var client = factory.CreateClientFor("svc");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode);

        await db.GrantAsync("svc", "orders", "dev");
        await Eventually.AssertAsync(async () =>
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode));

        await db.RevokeAsync("svc");
        await Eventually.AssertAsync(async () =>
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode));
    }

    [Fact]
    public async Task Starts_without_tables_and_recovers_once_they_exist()
    {
        var (db, factory) = await StartAsync(withSchema: false, poll: TimeSpan.FromMilliseconds(300));
        await using var disposable = factory;
        using var client = factory.CreateClientFor("svc");

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode);

        await db.CreateSchemaAsync();
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1));
        await db.GrantAsync("svc", "orders", "dev");

        await Eventually.AssertAsync(async () =>
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/snapshots/orders/dev")).StatusCode));
    }

    [Fact]
    public async Task Health_endpoints_are_anonymous()
    {
        var (_, factory) = await StartAsync();
        await using var disposable = factory;
        using var client = factory.CreateClientFor(null);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Ready_is_503_when_database_is_unreachable()
    {
        await using var factory = new DistributionFactory("Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=2");
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
}

public static class Eventually
{
    public static async Task AssertAsync(Func<Task> assertion, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (true)
        {
            try
            {
                await assertion();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100);
            }
        }
    }
}

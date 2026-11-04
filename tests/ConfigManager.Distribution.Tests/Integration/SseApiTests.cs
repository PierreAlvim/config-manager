using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;

namespace ConfigManager.Distribution.Tests.Integration;

public class SseApiTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Url = "/api/v1/snapshots/orders/dev/stream";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<HttpResponseMessage> OpenAsync(HttpClient client, string url = Url) =>
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);

    /// <summary>Reads one SSE frame (up to the blank line) as raw lines.</summary>
    private static async Task<string[]> ReadFrameAsync(StreamReader reader)
    {
        using var cts = new CancellationTokenSource(Timeout);
        var lines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token) ?? throw new EndOfStreamException();
            if (line.Length == 0)
            {
                if (lines.Count == 0) continue;
                return lines.ToArray();
            }
            lines.Add(line);
        }
    }

    private static async Task<(long Id, string Data)> ReadSnapshotAsync(StreamReader reader)
    {
        while (true)
        {
            var frame = await ReadFrameAsync(reader);
            if (frame[0] != "event: snapshot") continue;
            Assert.StartsWith("id: ", frame[1]);
            Assert.StartsWith("data: ", frame[2]);
            Assert.Equal(3, frame.Length); // single-line data
            return (long.Parse(frame[1][4..]), frame[2][6..]);
        }
    }

    [Fact]
    public async Task Sends_initial_snapshot_then_new_versions_on_notification()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        await using var factory = new DistributionFactory(db.ConnectionString);
        using var client = factory.CreateClientFor("svc");

        using var response = await OpenAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl!.ToString());
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        var (id, data) = await ReadSnapshotAsync(reader);
        Assert.Equal(1, id);
        Assert.Equal(1, JsonDocument.Parse(data).RootElement.GetProperty("version").GetInt64());

        await db.PublishAsync(TestData.Snapshot("orders", "dev", 2));

        var (nextId, nextData) = await ReadSnapshotAsync(reader);
        Assert.Equal(2, nextId);
        Assert.Equal(2, JsonDocument.Parse(nextData).RootElement.GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task Sends_keepalive_comments_when_idle()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        await using var factory = new DistributionFactory(db.ConnectionString, keepAlive: TimeSpan.FromMilliseconds(200));
        using var client = factory.CreateClientFor("svc");

        using var response = await OpenAsync(client);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        await ReadSnapshotAsync(reader);

        Assert.Equal([": keepalive"], await ReadFrameAsync(reader));
    }

    [Fact]
    public async Task Only_authorized_clients_can_stream()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        await using var factory = new DistributionFactory(db.ConnectionString);
        using var anonymous = factory.CreateClientFor(null);
        using var stranger = factory.CreateClientFor("stranger");
        using var svc = factory.CreateClientFor("svc");

        Assert.Equal(HttpStatusCode.Unauthorized, (await OpenAsync(anonymous)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OpenAsync(stranger)).StatusCode);
        using var missing = await OpenAsync(svc, "/api/v1/snapshots/orders/prod/stream");
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }

    [Fact]
    public async Task Unknown_pair_with_wildcard_grant_is_404()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.GrantAsync("svc", "orders", "*", notify: false);
        await using var factory = new DistributionFactory(db.ConnectionString);
        using var client = factory.CreateClientFor("svc");

        Assert.Equal(HttpStatusCode.NotFound, (await OpenAsync(client, "/api/v1/snapshots/orders/prod/stream")).StatusCode);
    }

    [Fact]
    public async Task Stream_unsubscribes_when_client_disconnects()
    {
        var db = await postgres.CreateDatabaseAsync();
        await db.PublishAsync(TestData.Snapshot("orders", "dev", 1), notify: false);
        await db.GrantAsync("svc", "orders", "dev", notify: false);
        await using var factory = new DistributionFactory(db.ConnectionString);
        using var client = factory.CreateClientFor("svc");
        var broadcaster = factory.Services.GetRequiredService<ConfigManager.Distribution.Api.Caching.SnapshotBroadcaster>();

        var response = await OpenAsync(client);
        using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
            await ReadSnapshotAsync(reader);
        Assert.Equal(1, broadcaster.SubscriberCount);

        response.Dispose();

        await Eventually.AssertAsync(() =>
        {
            Assert.Equal(0, broadcaster.SubscriberCount);
            return Task.CompletedTask;
        });
    }
}

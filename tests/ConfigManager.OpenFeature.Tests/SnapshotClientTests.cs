using System.Net;
using System.Text;

namespace ConfigManager.OpenFeature.Tests;

public class SnapshotClientTests
{
    private static (SnapshotClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> snapshots)
    {
        var handler = new FakeHandler(r => r.RequestUri!.ToString() == T.TokenUrl ? T.Token() : snapshots(r));
        var http = new HttpClient(handler);
        var options = T.Options();
        return (new SnapshotClient(http, new ClientCredentialsTokenProvider(http, options), options), handler);
    }

    [Fact]
    public async Task Get_sends_bearer_and_returns_snapshot_with_etag()
    {
        var (client, handler) = Create(_ => T.Json(T.SnapshotJson(3), etag: "\"3\""));
        var result = await client.GetSnapshotAsync(null);

        Assert.Equal(3, result.Snapshot!.Version);
        Assert.Equal("\"3\"", result.ETag);
        var req = handler.Requests.Single(r => r.Method == HttpMethod.Get);
        Assert.Equal("http://dist.test/api/v1/snapshots/orders/dev", req.Url);
        Assert.Equal("Bearer tok", req.Headers["Authorization"]);
        Assert.False(req.Headers.ContainsKey("If-None-Match"));
    }

    [Fact]
    public async Task Get_sends_if_none_match_and_handles_304()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotModified));
        var result = await client.GetSnapshotAsync("\"3\"");

        Assert.True(result.NotModified);
        Assert.Equal("\"3\"", handler.Requests.Last().Headers["If-None-Match"]);
    }

    [Fact]
    public async Task Get_non_success_throws_and_401_invalidates_token()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetSnapshotAsync(null));
        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetSnapshotAsync(null));
        Assert.Equal(2, handler.Count("/token")); // token re-requested after 401
    }

    [Fact]
    public async Task Stream_delivers_snapshot_events_only()
    {
        var one = T.SnapshotJson(1).ReplaceLineEndings("").Replace("  ", "");
        var two = T.SnapshotJson(2).ReplaceLineEndings("").Replace("  ", "");
        var body = $": hello\n\nevent: snapshot\nid: 1\ndata: {one}\n\n: keepalive\n\nevent: other\ndata: x\n\nevent: snapshot\nid: 2\ndata: {two}\n\n";
        var (client, handler) = Create(_ => T.Sse(body));

        var versions = new List<long>();
        await client.StreamAsync(s => { versions.Add(s.Version); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal([1L, 2L], versions);
        var req = handler.Requests.Last();
        Assert.Equal("http://dist.test/api/v1/snapshots/orders/dev/stream", req.Url);
        Assert.Equal("text/event-stream", req.Headers["Accept"]);
    }

    [Fact]
    public async Task Stream_skips_malformed_data()
    {
        var good = T.SnapshotJson(2).ReplaceLineEndings("");
        var (client, _) = Create(_ => T.Sse($"event: snapshot\ndata: {{not json\n\nevent: snapshot\ndata: {good}\n\n"));
        var versions = new List<long>();
        await client.StreamAsync(s => { versions.Add(s.Version); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal([2L], versions);
    }

    [Fact]
    public async Task SseReader_parses_multiline_data_comments_ids_and_crlf()
    {
        var text = ": c\r\n\r\nid: 7\r\nevent: snapshot\r\ndata: line1\r\ndata:line2\r\ndata\r\n\r\ndata: tail\n\n";
        var messages = new List<SseMessage>();
        await foreach (var m in SseReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), TimeSpan.FromSeconds(5)))
            messages.Add(m);

        Assert.Equal(2, messages.Count);
        Assert.Equal(new SseMessage("snapshot", "line1\nline2\n", "7"), messages[0]);
        Assert.Equal("message", messages[1].Event);
        Assert.Equal("tail", messages[1].Data);
    }

    [Fact]
    public async Task SseReader_ignores_incomplete_trailing_event()
    {
        var messages = new List<SseMessage>();
        await foreach (var m in SseReader.ReadAsync(new MemoryStream("data: x\n"u8.ToArray()), TimeSpan.FromSeconds(5)))
            messages.Add(m);
        Assert.Empty(messages);
    }
}

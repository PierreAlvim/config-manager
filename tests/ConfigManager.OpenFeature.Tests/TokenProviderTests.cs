namespace ConfigManager.OpenFeature.Tests;

public class TokenProviderTests
{
    [Fact]
    public async Task Posts_client_credentials_form_and_caches_until_30s_before_expiry()
    {
        var time = new ManualTime();
        var n = 0;
        var handler = new FakeHandler(_ => T.Token($"tok{++n}", expiresIn: 100));
        var options = T.Options(o => o.Scope = "dist");
        var sut = new ClientCredentialsTokenProvider(new HttpClient(handler), options, time);

        Assert.Equal("tok1", await sut.GetTokenAsync());
        time.Advance(TimeSpan.FromSeconds(69));
        Assert.Equal("tok1", await sut.GetTokenAsync());
        Assert.Single(handler.Requests);

        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(T.TokenUrl, req.Url);
        Assert.Contains("grant_type=client_credentials", req.Body);
        Assert.Contains("client_id=orders-service", req.Body);
        Assert.Contains("client_secret=secret", req.Body);
        Assert.Contains("scope=dist", req.Body);

        time.Advance(TimeSpan.FromSeconds(2)); // 71s > 100-30
        Assert.Equal("tok2", await sut.GetTokenAsync());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Invalidate_forces_new_token()
    {
        var n = 0;
        var handler = new FakeHandler(_ => T.Token($"tok{++n}"));
        var sut = new ClientCredentialsTokenProvider(new HttpClient(handler), T.Options());
        await sut.GetTokenAsync();
        sut.Invalidate();
        Assert.Equal("tok2", await sut.GetTokenAsync());
    }

    [Fact]
    public async Task Concurrent_callers_share_a_single_request()
    {
        var handler = new FakeHandler(async _ => { await Task.Delay(100); return T.Token(); });
        var sut = new ClientCredentialsTokenProvider(new HttpClient(handler), T.Options());
        var tokens = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => sut.GetTokenAsync().AsTask()));
        Assert.All(tokens, t => Assert.Equal("tok", t));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Failure_throws_and_is_not_cached()
    {
        var fail = true;
        var handler = new FakeHandler(_ => fail ? T.Json("{}", System.Net.HttpStatusCode.Unauthorized) : T.Token());
        var sut = new ClientCredentialsTokenProvider(new HttpClient(handler), T.Options());
        await Assert.ThrowsAsync<HttpRequestException>(async () => await sut.GetTokenAsync());
        fail = false;
        Assert.Equal("tok", await sut.GetTokenAsync());
    }
}

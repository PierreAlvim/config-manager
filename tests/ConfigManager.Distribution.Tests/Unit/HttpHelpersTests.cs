using System.Security.Claims;
using ConfigManager.Distribution.Api.Background;
using ConfigManager.Distribution.Api.Caching;
using ConfigManager.Distribution.Api.Data;
using ConfigManager.Distribution.Api.Http;
using Microsoft.Extensions.Primitives;

namespace ConfigManager.Distribution.Tests.Unit;

public class ETagsTests
{
    [Fact]
    public void Formats_quoted_version() => Assert.Equal("\"12\"", ETags.ForVersion(12));

    [Theory]
    [InlineData("\"3\"", true)]
    [InlineData("W/\"3\"", true)]
    [InlineData("\"1\", \"3\"", true)]
    [InlineData("*", true)]
    [InlineData("\"4\"", false)]
    [InlineData("3", false)]
    [InlineData("", false)]
    public void If_none_match(string header, bool expected) =>
        Assert.Equal(expected, ETags.Matches(new StringValues(header), "\"3\""));

    [Fact]
    public void Absent_header_never_matches() => Assert.False(ETags.Matches(StringValues.Empty, "\"3\""));
}

public class AccessGrantsTests
{
    private readonly AccessGrants _grants = new([
        new AccessGrant("orders-service", "orders", "dev"),
        new AccessGrant("payments-service", "payments", "*"),
    ]);

    [Theory]
    [InlineData("orders-service", "orders", "dev", true)]
    [InlineData("orders-service", "orders", "prod", false)]
    [InlineData("orders-service", "payments", "dev", false)]
    [InlineData("payments-service", "payments", "prod", true)]
    [InlineData("payments-service", "payments", "dev", true)]
    [InlineData("payments-service", "orders", "dev", false)]
    [InlineData("other", "orders", "dev", false)]
    public void Matches_exact_and_wildcard_environment(string client, string ns, string env, bool expected) =>
        Assert.Equal(expected, _grants.IsAllowed(client, ns, env));
}

public class ClientIdTests
{
    private static ClaimsPrincipal User(params (string, string)[] claims) =>
        new(new ClaimsIdentity(claims.Select(c => new Claim(c.Item1, c.Item2)), "test"));

    [Fact]
    public void Prefers_azp() => Assert.Equal("a", ClientIds.Resolve(User(("azp", "a"), ("client_id", "b"))));

    [Fact]
    public void Falls_back_to_client_id() => Assert.Equal("b", ClientIds.Resolve(User(("client_id", "b"))));

    [Fact]
    public void Null_without_claims() => Assert.Null(ClientIds.Resolve(User()));
}

public class SseFormatterTests
{
    [Fact]
    public void Snapshot_frame() =>
        Assert.Equal("event: snapshot\nid: 5\ndata: {\"a\":1}\n\n", SseFormatter.Snapshot(5, "{\"a\":1}"));

    [Fact]
    public void Multiline_data_is_split_into_data_lines() =>
        Assert.Equal("event: snapshot\nid: 1\ndata: a\ndata: b\n\n", SseFormatter.Snapshot(1, "a\r\nb"));

    [Fact]
    public void Keepalive_is_a_comment() => Assert.Equal(": keepalive\n\n", SseFormatter.KeepAlive);
}

public class NotificationPayloadTests
{
    [Fact]
    public void Parses_published_payload()
    {
        Assert.True(NotificationDispatcher.TryParsePublished("orders/dev/12", out var ns, out var env, out var v));
        Assert.Equal(("orders", "dev", 12L), (ns, env, v));
    }

    [Theory]
    [InlineData("")]
    [InlineData("orders/dev")]
    [InlineData("orders/dev/x")]
    [InlineData("/dev/1")]
    [InlineData("a/b/c/1")]
    public void Rejects_malformed_payload(string payload) =>
        Assert.False(NotificationDispatcher.TryParsePublished(payload, out _, out _, out _));
}

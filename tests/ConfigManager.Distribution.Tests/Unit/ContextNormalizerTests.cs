using ConfigManager.Distribution.Api.Ofrep;

namespace ConfigManager.Distribution.Tests.Unit;

public class ContextNormalizerTests
{
    [Fact]
    public void Normalizes_scalars_to_strings_and_skips_nested_values()
    {
        const string body = """
            { "context": {
                "targetingKey": "user-1", "tenant": "acme", "beta": true, "legacy": false,
                "age": 42, "ratio": 1.5, "nothing": null, "obj": { "a": 1 }, "list": [1, 2] } }
            """;

        Assert.True(ContextNormalizer.TryParseRequest(body, out var input, out var error));

        Assert.Null(error);
        Assert.Equal("user-1", input.TargetingKey);
        Assert.Equal("acme", input.Get("tenant"));
        Assert.Equal("true", input.Get("beta"));
        Assert.Equal("false", input.Get("legacy"));
        Assert.Equal("42", input.Get("age"));
        Assert.Equal("1.5", input.Get("ratio"));
        Assert.Null(input.Get("nothing"));
        Assert.Null(input.Get("obj"));
        Assert.Null(input.Get("list"));
        Assert.DoesNotContain("targetingKey", input.Attributes.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("{}")]
    [InlineData("""{ "context": null }""")]
    public void Missing_context_is_empty(string? body)
    {
        Assert.True(ContextNormalizer.TryParseRequest(body, out var input, out _));
        Assert.Null(input.TargetingKey);
        Assert.Empty(input.Attributes);
    }

    [Theory]
    [InlineData("{ nope")]
    [InlineData("[1]")]
    [InlineData("""{ "context": "x" }""")]
    public void Invalid_bodies_report_an_error(string body)
    {
        Assert.False(ContextNormalizer.TryParseRequest(body, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Numbers_use_invariant_culture()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
        try
        {
            ContextNormalizer.TryParseRequest("""{ "context": { "n": 2.25 } }""", out var input, out _);
            Assert.Equal("2.25", input.Get("n"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }
}

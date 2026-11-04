using System.Text.Json;
using ConfigManager.Distribution.Api.Ofrep;

namespace ConfigManager.Distribution.Tests.Unit;

public class OfrepMapperTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    [Theory]
    [InlineData(EvaluationReason.Static, "STATIC")]
    [InlineData(EvaluationReason.Default, "DEFAULT")]
    [InlineData(EvaluationReason.TargetingMatch, "TARGETING_MATCH")]
    [InlineData(EvaluationReason.Split, "SPLIT")]
    [InlineData(EvaluationReason.Disabled, "DISABLED")]
    [InlineData(EvaluationReason.Error, "ERROR")]
    public void Reason_names_are_upper_snake(EvaluationReason reason, string expected) =>
        Assert.Equal(expected, OfrepMapper.ReasonName(reason));

    [Theory]
    [InlineData(EvaluationErrorCode.FlagNotFound, "FLAG_NOT_FOUND", 404)]
    [InlineData(EvaluationErrorCode.ParseError, "PARSE_ERROR", 400)]
    [InlineData(EvaluationErrorCode.TypeMismatch, "TYPE_MISMATCH", 400)]
    [InlineData(EvaluationErrorCode.General, "GENERAL", 500)]
    public void Error_codes_map_to_names_and_status(EvaluationErrorCode code, string name, int status)
    {
        var mapped = OfrepMapper.Map("k", EvaluationResult.Fail(code, "boom"), 3);

        Assert.Equal(status, mapped.StatusCode);
        Assert.Equal(name, mapped.Body["errorCode"]);
        Assert.Equal("boom", mapped.Body["errorDetails"]);
        Assert.Equal("k", mapped.Body["key"]);
        Assert.False(mapped.Body.ContainsKey("value"));
    }

    [Fact]
    public void Success_carries_value_variant_reason_and_version()
    {
        var result = new EvaluationResult { Variant = "on", Value = Json("true"), Reason = EvaluationReason.TargetingMatch };

        var mapped = OfrepMapper.Map("flag", result, 7, new Dictionary<string, string> { ["owner"] = "team-a" });

        Assert.Equal(200, mapped.StatusCode);
        Assert.Equal("flag", mapped.Body["key"]);
        Assert.Equal("on", mapped.Body["variant"]);
        Assert.Equal("TARGETING_MATCH", mapped.Body["reason"]);
        Assert.True(((JsonElement)mapped.Body["value"]!).GetBoolean());
        var metadata = (Dictionary<string, object?>)mapped.Body["metadata"]!;
        Assert.Equal(7L, metadata["version"]);
        Assert.Equal("team-a", metadata["owner"]);
    }

    [Fact]
    public void Disabled_returns_null_value_without_variant()
    {
        var mapped = OfrepMapper.Map("flag", new EvaluationResult { Reason = EvaluationReason.Disabled }, 1);

        Assert.Equal(200, mapped.StatusCode);
        Assert.Equal("DISABLED", mapped.Body["reason"]);
        Assert.True(mapped.Body.ContainsKey("value"));
        Assert.Null(mapped.Body["value"]);
        Assert.False(mapped.Body.ContainsKey("variant"));
    }

    [Fact]
    public void Bulk_etag_depends_on_version_and_context_but_not_attribute_order()
    {
        var a = new EvaluationInput("u1", new Dictionary<string, string?> { ["x"] = "1", ["y"] = "2" });
        var b = new EvaluationInput("u1", new Dictionary<string, string?> { ["y"] = "2", ["x"] = "1" });
        var c = new EvaluationInput("u2", a.Attributes);

        Assert.Equal(OfrepMapper.BulkETag(3, a), OfrepMapper.BulkETag(3, b));
        Assert.NotEqual(OfrepMapper.BulkETag(3, a), OfrepMapper.BulkETag(4, a));
        Assert.NotEqual(OfrepMapper.BulkETag(3, a), OfrepMapper.BulkETag(3, c));
        Assert.StartsWith("\"3-", OfrepMapper.BulkETag(3, a));
    }
}

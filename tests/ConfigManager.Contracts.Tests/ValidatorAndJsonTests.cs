using System.Text.Json;
using ConfigManager.Contracts;

namespace ConfigManager.Contracts.Tests;

public class ValidatorAndJsonTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static FlagDefinition Valid() => new()
    {
        Type = FlagType.Integer,
        Variants = new() { ["small"] = J("10"), ["large"] = J("50") },
        DefaultVariant = "small",
        Rules =
        [
            new() { Name = "vip", Conditions = [new() { Attribute = "plan", Operator = ConditionOperator.Equals, Values = ["vip"] }], Variant = "large" },
            new() { Rollout = [new() { Variant = "small", Weight = 70 }, new() { Variant = "large", Weight = 30 }] },
        ],
    };

    [Fact]
    public void Valid_flag_has_no_errors() => Assert.Empty(FlagValidator.Validate("max-items", Valid()));

    [Theory]
    [InlineData("Bad-Key")]
    [InlineData("-x")]
    [InlineData("")]
    [InlineData("has space")]
    public void Invalid_keys_are_rejected(string key) => Assert.NotEmpty(FlagValidator.Validate(key, Valid()));

    [Theory]
    [InlineData("a")]
    [InlineData("new-checkout")]
    [InlineData("svc.feature_x-1")]
    public void Valid_keys_are_accepted(string key) => Assert.True(KeyRules.IsValidKey(key));

    [Fact]
    public void Type_mismatch_is_reported()
    {
        var flag = Valid() with { Variants = new() { ["small"] = J("10.5"), ["large"] = J("\"50\"") } };
        var errors = FlagValidator.Validate("f", flag);
        Assert.Contains(errors, e => e.Contains("'small'"));
        Assert.Contains(errors, e => e.Contains("'large'"));
    }

    [Fact]
    public void Unknown_default_and_rule_variants_are_reported()
    {
        var flag = Valid() with
        {
            DefaultVariant = "nope",
            Rules = [new() { Variant = "ghost" }],
        };
        var errors = FlagValidator.Validate("f", flag);
        Assert.Contains(errors, e => e.Contains("Default variant 'nope'"));
        Assert.Contains(errors, e => e.Contains("'ghost'"));
    }

    [Fact]
    public void Rollout_must_sum_100_and_rule_needs_exactly_one_target()
    {
        var flag = Valid() with
        {
            Rules =
            [
                new() { Rollout = [new() { Variant = "small", Weight = 60 }] },
                new() { },
                new() { Variant = "small", Rollout = [new() { Variant = "small", Weight = 100 }] },
            ],
        };
        var errors = FlagValidator.Validate("f", flag);
        Assert.Contains(errors, e => e.Contains("sum to 100"));
        Assert.Equal(2, errors.Count(e => e.Contains("exactly one")));
    }

    [Theory]
    [InlineData(FlagType.Boolean, "true", true)]
    [InlineData(FlagType.Boolean, "\"true\"", false)]
    [InlineData(FlagType.String, "\"x\"", true)]
    [InlineData(FlagType.Integer, "3", true)]
    [InlineData(FlagType.Integer, "3.2", false)]
    [InlineData(FlagType.Double, "3", true)]
    [InlineData(FlagType.Object, "{\"a\":1}", true)]
    [InlineData(FlagType.Object, "[1,2]", true)]
    [InlineData(FlagType.Object, "1", false)]
    public void Value_type_matching(FlagType type, string json, bool ok) =>
        Assert.Equal(ok, FlagValidator.ValueMatchesType(type, J(json)));

    [Fact]
    public void Snapshot_round_trips_with_camelCase_and_string_enums()
    {
        var snapshot = new ConfigSnapshot
        {
            Namespace = "orders", Environment = "dev", Version = 7, PublishedAt = DateTimeOffset.Parse("2026-11-04T10:00:00Z"),
            Flags = { ["max-items"] = Valid() },
        };

        var json = SnapshotJson.Serialize(snapshot);
        Assert.Contains("\"type\":\"integer\"", json);
        Assert.Contains("\"operator\":\"equals\"", json);
        Assert.Contains("\"defaultVariant\":\"small\"", json);
        Assert.DoesNotContain("\"variant\":null", json);

        var back = SnapshotJson.Deserialize(json);
        Assert.Equal(7, back.Version);
        Assert.Equal(50, back.Flags["max-items"].Variants["large"].GetInt32());
        Assert.Equal(ConditionOperator.Equals, back.Flags["max-items"].Rules[0].Conditions[0].Operator);
    }

    [Fact]
    public void Deserializes_documented_example()
    {
        const string json = """
        { "namespace": "orders", "environment": "dev", "version": 3, "publishedAt": "2026-11-04T10:00:00Z",
          "flags": { "new-checkout": { "type": "boolean", "enabled": true, "variants": { "on": true, "off": false },
            "defaultVariant": "off",
            "rules": [ { "name": "beta", "conditions": [ { "attribute": "tenant", "operator": "in", "values": ["acme"] } ], "variant": "on" },
                       { "rollout": [ { "variant": "on", "weight": 10 }, { "variant": "off", "weight": 90 } ] } ] } } }
        """;
        var s = SnapshotJson.Deserialize(json);
        var r = FlagEvaluator.Evaluate(s, "new-checkout", new EvaluationInput("u", new Dictionary<string, string?> { ["tenant"] = "acme" }));
        Assert.Equal(("on", EvaluationReason.TargetingMatch), (r.Variant, r.Reason));
        Assert.Empty(FlagValidator.Validate("new-checkout", s.Flags["new-checkout"]));
    }
}

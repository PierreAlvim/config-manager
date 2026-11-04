using System.Text.Json;
using ConfigManager.Contracts;

namespace ConfigManager.Contracts.Tests;

public class FlagEvaluatorTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static FlagDefinition BoolFlag(params TargetingRule[] rules) => new()
    {
        Type = FlagType.Boolean,
        Variants = new() { ["on"] = J("true"), ["off"] = J("false") },
        DefaultVariant = "off",
        Rules = rules.ToList(),
    };

    private static EvaluationInput Ctx(string? key = null, params (string k, string v)[] attrs) =>
        new(key, attrs.ToDictionary(a => a.k, a => (string?)a.v));

    [Fact]
    public void No_rules_returns_default_with_static_reason()
    {
        var r = FlagEvaluator.Evaluate(BoolFlag(), "f", EvaluationInput.Empty);
        Assert.Equal("off", r.Variant);
        Assert.Equal(EvaluationReason.Static, r.Reason);
        Assert.False(r.Value!.Value.GetBoolean());
    }

    [Fact]
    public void Disabled_flag_returns_no_value()
    {
        var r = FlagEvaluator.Evaluate(BoolFlag() with { Enabled = false }, "f", EvaluationInput.Empty);
        Assert.Equal(EvaluationReason.Disabled, r.Reason);
        Assert.Null(r.Value);
        Assert.Null(r.Variant);
    }

    [Fact]
    public void Matching_rule_returns_targeting_match()
    {
        var flag = BoolFlag(new TargetingRule
        {
            Conditions = [new Condition { Attribute = "tenant", Operator = ConditionOperator.In, Values = ["acme", "globex"] }],
            Variant = "on",
        });

        var hit = FlagEvaluator.Evaluate(flag, "f", Ctx(null, ("tenant", "globex")));
        var miss = FlagEvaluator.Evaluate(flag, "f", Ctx(null, ("tenant", "other")));

        Assert.Equal(("on", EvaluationReason.TargetingMatch), (hit.Variant, hit.Reason));
        Assert.Equal(("off", EvaluationReason.Default), (miss.Variant, miss.Reason));
    }

    [Fact]
    public void All_conditions_must_match()
    {
        var flag = BoolFlag(new TargetingRule
        {
            Conditions =
            [
                new Condition { Attribute = "tenant", Operator = ConditionOperator.Equals, Values = ["acme"] },
                new Condition { Attribute = "country", Operator = ConditionOperator.Equals, Values = ["BR"] },
            ],
            Variant = "on",
        });

        Assert.Equal("off", FlagEvaluator.Evaluate(flag, "f", Ctx(null, ("tenant", "acme"))).Variant);
        Assert.Equal("on", FlagEvaluator.Evaluate(flag, "f", Ctx(null, ("tenant", "acme"), ("country", "BR"))).Variant);
    }

    [Fact]
    public void First_matching_rule_wins()
    {
        var flag = BoolFlag(
            new TargetingRule { Conditions = [], Variant = "on" },
            new TargetingRule { Conditions = [], Variant = "off" });
        Assert.Equal("on", FlagEvaluator.Evaluate(flag, "f", EvaluationInput.Empty).Variant);
    }

    [Fact]
    public void Rollout_is_deterministic_and_skipped_without_targeting_key()
    {
        var flag = BoolFlag(new TargetingRule
        {
            Rollout = [new WeightedVariant { Variant = "on", Weight = 50 }, new WeightedVariant { Variant = "off", Weight = 50 }],
        });

        var a = FlagEvaluator.Evaluate(flag, "f", Ctx("user-42"));
        var b = FlagEvaluator.Evaluate(flag, "f", Ctx("user-42"));
        Assert.Equal(EvaluationReason.Split, a.Reason);
        Assert.Equal(a.Variant, b.Variant);

        var noKey = FlagEvaluator.Evaluate(flag, "f", EvaluationInput.Empty);
        Assert.Equal(EvaluationReason.Default, noKey.Reason);
    }

    [Fact]
    public void Rollout_distribution_roughly_matches_weights()
    {
        var rollout = new List<WeightedVariant> { new() { Variant = "on", Weight = 10 }, new() { Variant = "off", Weight = 90 } };
        var on = Enumerable.Range(0, 10_000).Count(i => FlagEvaluator.PickRolloutVariant("flag", $"user-{i}", rollout) == "on");
        Assert.InRange(on, 850, 1150);
    }

    [Fact]
    public void Bucket_depends_on_flag_key()
    {
        var differs = Enumerable.Range(0, 100).Any(i => FlagEvaluator.GetBucket("a", $"u{i}") != FlagEvaluator.GetBucket("b", $"u{i}"));
        Assert.True(differs);
        Assert.All(Enumerable.Range(0, 1000), i => Assert.InRange(FlagEvaluator.GetBucket("a", $"u{i}"), 0, 99));
    }

    [Fact]
    public void Missing_flag_in_snapshot_is_flag_not_found()
    {
        var snapshot = new ConfigSnapshot { Namespace = "n", Environment = "e", Version = 1, PublishedAt = DateTimeOffset.UnixEpoch };
        var r = FlagEvaluator.Evaluate(snapshot, "missing", EvaluationInput.Empty);
        Assert.Equal(EvaluationReason.Error, r.Reason);
        Assert.Equal(EvaluationErrorCode.FlagNotFound, r.ErrorCode);
    }

    [Fact]
    public void Undefined_variant_yields_general_error()
    {
        var flag = BoolFlag() with { DefaultVariant = "ghost" };
        Assert.Equal(EvaluationErrorCode.General, FlagEvaluator.Evaluate(flag, "f", EvaluationInput.Empty).ErrorCode);
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "10", "10.0", true)]
    [InlineData(ConditionOperator.Equals, "abc", "abc", true)]
    [InlineData(ConditionOperator.NotEquals, "abc", "abd", true)]
    [InlineData(ConditionOperator.NotIn, "x", "a", true)]
    [InlineData(ConditionOperator.Contains, "hello world", "lo w", true)]
    [InlineData(ConditionOperator.StartsWith, "user@acme.com", "user@", true)]
    [InlineData(ConditionOperator.EndsWith, "user@acme.com", "@acme.com", true)]
    [InlineData(ConditionOperator.GreaterThan, "18", "17.5", true)]
    [InlineData(ConditionOperator.GreaterThan, "abc", "1", false)]
    [InlineData(ConditionOperator.GreaterThanOrEquals, "18", "18", true)]
    [InlineData(ConditionOperator.LessThan, "1e2", "200", true)]
    [InlineData(ConditionOperator.LessThanOrEquals, "5", "4", false)]
    public void Operators(ConditionOperator op, string actual, string expected, bool result)
    {
        var c = new Condition { Attribute = "a", Operator = op, Values = [expected] };
        Assert.Equal(result, FlagEvaluator.Matches(c, Ctx(null, ("a", actual))));
    }

    [Fact]
    public void Missing_attribute_only_matches_negative_operators()
    {
        Assert.False(FlagEvaluator.Matches(new Condition { Attribute = "x", Operator = ConditionOperator.Equals, Values = ["1"] }, EvaluationInput.Empty));
        Assert.True(FlagEvaluator.Matches(new Condition { Attribute = "x", Operator = ConditionOperator.NotIn, Values = ["1"] }, EvaluationInput.Empty));
        Assert.False(FlagEvaluator.Matches(new Condition { Attribute = "x", Operator = ConditionOperator.GreaterThan, Values = ["1"] }, EvaluationInput.Empty));
    }

    [Fact]
    public void TargetingKey_attribute_reads_targeting_key()
    {
        var c = new Condition { Attribute = "targetingKey", Operator = ConditionOperator.In, Values = ["u1"] };
        Assert.True(FlagEvaluator.Matches(c, Ctx("u1")));
    }
}

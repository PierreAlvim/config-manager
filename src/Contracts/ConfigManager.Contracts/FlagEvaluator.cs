using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ConfigManager.Contracts;

/// <summary>Reason codes aligned with OpenFeature's standard reasons.</summary>
public enum EvaluationReason
{
    Static,
    Default,
    TargetingMatch,
    Split,
    Disabled,
    Error,
}

/// <summary>Error codes aligned with OpenFeature's standard error codes.</summary>
public enum EvaluationErrorCode
{
    None,
    FlagNotFound,
    TypeMismatch,
    ParseError,
    General,
}

public sealed record EvaluationResult
{
    public string? Variant { get; init; }
    /// <summary>Resolved variant value; null when Disabled or Error (caller should use its own default).</summary>
    public JsonElement? Value { get; init; }
    public EvaluationReason Reason { get; init; }
    public EvaluationErrorCode ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }

    public static EvaluationResult Fail(EvaluationErrorCode code, string message) =>
        new() { Reason = EvaluationReason.Error, ErrorCode = code, ErrorMessage = message };
}

/// <summary>
/// Context passed to the evaluator. Attribute values are pre-normalized to strings
/// (booleans "true"/"false", numbers with invariant culture).
/// </summary>
public sealed record EvaluationInput(string? TargetingKey, IReadOnlyDictionary<string, string?> Attributes)
{
    public static readonly EvaluationInput Empty = new(null, new Dictionary<string, string?>());

    public string? Get(string attribute) =>
        attribute == TargetingKeyAttribute
            ? TargetingKey
            : Attributes.TryGetValue(attribute, out var v) ? v : null;

    public const string TargetingKeyAttribute = "targetingKey";
}

public static class FlagEvaluator
{
    /// <summary>Evaluates a flag from a snapshot. Missing flag -> FlagNotFound error.</summary>
    public static EvaluationResult Evaluate(ConfigSnapshot snapshot, string flagKey, EvaluationInput input) =>
        snapshot.Flags.TryGetValue(flagKey, out var flag)
            ? Evaluate(flag, flagKey, input)
            : EvaluationResult.Fail(EvaluationErrorCode.FlagNotFound, $"Flag '{flagKey}' not found");

    public static EvaluationResult Evaluate(FlagDefinition flag, string flagKey, EvaluationInput input)
    {
        if (!flag.Enabled)
            return new EvaluationResult { Reason = EvaluationReason.Disabled };

        foreach (var rule in flag.Rules)
        {
            if (!rule.Conditions.All(c => Matches(c, input)))
                continue;

            if (rule.Rollout is { Count: > 0 } rollout)
            {
                if (string.IsNullOrEmpty(input.TargetingKey))
                    continue; // cannot bucket without a targeting key: skip rule
                var variant = PickRolloutVariant(flagKey, input.TargetingKey, rollout);
                if (variant is null) continue;
                return Resolve(flag, variant, EvaluationReason.Split);
            }

            if (rule.Variant is not null)
                return Resolve(flag, rule.Variant, EvaluationReason.TargetingMatch);
        }

        return Resolve(flag, flag.DefaultVariant, flag.Rules.Count == 0 ? EvaluationReason.Static : EvaluationReason.Default);
    }

    private static EvaluationResult Resolve(FlagDefinition flag, string variant, EvaluationReason reason) =>
        flag.Variants.TryGetValue(variant, out var value)
            ? new EvaluationResult { Variant = variant, Value = value, Reason = reason }
            : EvaluationResult.Fail(EvaluationErrorCode.General, $"Variant '{variant}' is not defined");

    /// <summary>Deterministic bucket in [0, 99] for a (flag, targetingKey) pair.</summary>
    public static int GetBucket(string flagKey, string targetingKey)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes($"{flagKey}:{targetingKey}"), hash);
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % 100);
    }

    public static string? PickRolloutVariant(string flagKey, string targetingKey, IReadOnlyList<WeightedVariant> rollout)
    {
        var bucket = GetBucket(flagKey, targetingKey);
        var cumulative = 0;
        foreach (var wv in rollout)
        {
            cumulative += wv.Weight;
            if (bucket < cumulative) return wv.Variant;
        }
        return null;
    }

    public static bool Matches(Condition condition, EvaluationInput input)
    {
        var actual = input.Get(condition.Attribute);
        var values = condition.Values;
        var first = values.Count > 0 ? values[0] : null;

        return condition.Operator switch
        {
            ConditionOperator.Equals => actual is not null && first is not null && ValueEquals(actual, first),
            ConditionOperator.NotEquals => actual is null || first is null || !ValueEquals(actual, first),
            ConditionOperator.In => actual is not null && values.Any(v => ValueEquals(actual, v)),
            ConditionOperator.NotIn => actual is null || !values.Any(v => ValueEquals(actual, v)),
            ConditionOperator.Contains => actual is not null && values.Any(v => actual.Contains(v, StringComparison.Ordinal)),
            ConditionOperator.StartsWith => actual is not null && values.Any(v => actual.StartsWith(v, StringComparison.Ordinal)),
            ConditionOperator.EndsWith => actual is not null && values.Any(v => actual.EndsWith(v, StringComparison.Ordinal)),
            ConditionOperator.GreaterThan => Compare(actual, first) is > 0,
            ConditionOperator.GreaterThanOrEquals => Compare(actual, first) is >= 0,
            ConditionOperator.LessThan => Compare(actual, first) is < 0,
            ConditionOperator.LessThanOrEquals => Compare(actual, first) is <= 0,
            _ => false,
        };
    }

    private static bool ValueEquals(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal)
        || (TryNumber(a, out var x) && TryNumber(b, out var y) && x == y);

    /// <summary>Numeric comparison; null when either side is missing or not numeric.</summary>
    private static int? Compare(string? a, string? b) =>
        a is not null && b is not null && TryNumber(a, out var x) && TryNumber(b, out var y) ? x.CompareTo(y) : null;

    private static bool TryNumber(string s, out double d) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
}

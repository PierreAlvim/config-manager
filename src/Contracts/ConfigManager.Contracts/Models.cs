using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigManager.Contracts;

/// <summary>Value type of a flag. Serialized as lowercase string ("boolean", "string", "integer", "double", "object").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FlagType>))]
public enum FlagType
{
    [JsonStringEnumMemberName("boolean")] Boolean,
    [JsonStringEnumMemberName("string")] String,
    [JsonStringEnumMemberName("integer")] Integer,
    [JsonStringEnumMemberName("double")] Double,
    [JsonStringEnumMemberName("object")] Object,
}

[JsonConverter(typeof(JsonStringEnumConverter<ConditionOperator>))]
public enum ConditionOperator
{
    [JsonStringEnumMemberName("equals")] Equals,
    [JsonStringEnumMemberName("notEquals")] NotEquals,
    [JsonStringEnumMemberName("in")] In,
    [JsonStringEnumMemberName("notIn")] NotIn,
    [JsonStringEnumMemberName("contains")] Contains,
    [JsonStringEnumMemberName("startsWith")] StartsWith,
    [JsonStringEnumMemberName("endsWith")] EndsWith,
    [JsonStringEnumMemberName("greaterThan")] GreaterThan,
    [JsonStringEnumMemberName("greaterThanOrEquals")] GreaterThanOrEquals,
    [JsonStringEnumMemberName("lessThan")] LessThan,
    [JsonStringEnumMemberName("lessThanOrEquals")] LessThanOrEquals,
}

/// <summary>Immutable, versioned, published configuration of one namespace in one environment.</summary>
public sealed record ConfigSnapshot
{
    public required string Namespace { get; init; }
    public required string Environment { get; init; }
    public required long Version { get; init; }
    public required DateTimeOffset PublishedAt { get; init; }
    public Dictionary<string, FlagDefinition> Flags { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>Full configuration of one flag inside one environment.</summary>
public sealed record FlagDefinition
{
    public required FlagType Type { get; init; }
    public bool Enabled { get; init; } = true;
    /// <summary>Variant name -> value. Value JSON kind must match <see cref="Type"/>.</summary>
    public Dictionary<string, JsonElement> Variants { get; init; } = new(StringComparer.Ordinal);
    public required string DefaultVariant { get; init; }
    /// <summary>Evaluated in order; first match wins.</summary>
    public List<TargetingRule> Rules { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>
/// A rule matches when ALL conditions match (empty list = always matches).
/// Exactly one of <see cref="Variant"/> or <see cref="Rollout"/> must be set.
/// </summary>
public sealed record TargetingRule
{
    public string? Name { get; init; }
    public List<Condition> Conditions { get; init; } = [];
    public string? Variant { get; init; }
    /// <summary>Percentage split; weights must sum to 100. Requires a targetingKey.</summary>
    public List<WeightedVariant>? Rollout { get; init; }
}

/// <summary>
/// Compares context attribute <see cref="Attribute"/> ("targetingKey" refers to the targeting key) against <see cref="Values"/>.
/// Values are strings; numeric operators parse both sides as invariant doubles.
/// </summary>
public sealed record Condition
{
    public required string Attribute { get; init; }
    public required ConditionOperator Operator { get; init; }
    public List<string> Values { get; init; } = [];
}

public sealed record WeightedVariant
{
    public required string Variant { get; init; }
    public required int Weight { get; init; }
}

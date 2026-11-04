using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConfigManager.Contracts;

public static partial class KeyRules
{
    /// <summary>Lowercase slug: letters, digits, '-', '_', '.'; must start/end with letter or digit; max 64 chars.</summary>
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$")]
    private static partial Regex KeyRegex();

    public static bool IsValidKey(string? key) => key is not null && KeyRegex().IsMatch(key);
}

public static class FlagValidator
{
    /// <summary>Returns human-readable validation errors; empty when valid.</summary>
    public static IReadOnlyList<string> Validate(string flagKey, FlagDefinition flag)
    {
        var errors = new List<string>();
        if (!KeyRules.IsValidKey(flagKey))
            errors.Add($"Invalid flag key '{flagKey}'.");

        if (flag.Variants.Count == 0)
            errors.Add("At least one variant is required.");

        foreach (var (name, value) in flag.Variants)
        {
            if (string.IsNullOrWhiteSpace(name))
                errors.Add("Variant names cannot be empty.");
            if (!ValueMatchesType(flag.Type, value))
                errors.Add($"Variant '{name}' value does not match type '{flag.Type.ToString().ToLowerInvariant()}'.");
        }

        if (!flag.Variants.ContainsKey(flag.DefaultVariant))
            errors.Add($"Default variant '{flag.DefaultVariant}' is not defined.");

        for (var i = 0; i < flag.Rules.Count; i++)
        {
            var rule = flag.Rules[i];
            var label = $"Rule #{i + 1}{(rule.Name is null ? "" : $" ('{rule.Name}')")}";
            var hasVariant = rule.Variant is not null;
            var hasRollout = rule.Rollout is { Count: > 0 };

            if (hasVariant == hasRollout)
                errors.Add($"{label}: exactly one of 'variant' or 'rollout' must be set.");
            if (hasVariant && !flag.Variants.ContainsKey(rule.Variant!))
                errors.Add($"{label}: variant '{rule.Variant}' is not defined.");
            if (hasRollout)
            {
                foreach (var wv in rule.Rollout!)
                {
                    if (!flag.Variants.ContainsKey(wv.Variant))
                        errors.Add($"{label}: rollout variant '{wv.Variant}' is not defined.");
                    if (wv.Weight < 0)
                        errors.Add($"{label}: rollout weights cannot be negative.");
                }
                var sum = rule.Rollout!.Sum(w => w.Weight);
                if (sum != 100)
                    errors.Add($"{label}: rollout weights must sum to 100 (got {sum}).");
            }

            foreach (var c in rule.Conditions)
            {
                if (string.IsNullOrWhiteSpace(c.Attribute))
                    errors.Add($"{label}: condition attribute cannot be empty.");
                if (c.Values.Count == 0)
                    errors.Add($"{label}: condition on '{c.Attribute}' requires at least one value.");
            }
        }

        return errors;
    }

    public static bool ValueMatchesType(FlagType type, JsonElement value) => type switch
    {
        FlagType.Boolean => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        FlagType.String => value.ValueKind == JsonValueKind.String,
        FlagType.Integer => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        FlagType.Double => value.ValueKind == JsonValueKind.Number,
        FlagType.Object => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
        _ => false,
    };
}

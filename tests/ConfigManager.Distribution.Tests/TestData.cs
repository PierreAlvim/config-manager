using System.Text.Json;

namespace ConfigManager.Distribution.Tests;

public static class TestData
{
    private static JsonElement J(string raw) => JsonDocument.Parse(raw).RootElement.Clone();

    public static ConfigSnapshot Snapshot(string ns, string env, long version, Dictionary<string, FlagDefinition>? flags = null) => new()
    {
        Namespace = ns,
        Environment = env,
        Version = version,
        PublishedAt = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
        Flags = flags ?? DefaultFlags(),
    };

    public static string Json(string ns, string env, long version) => SnapshotJson.Serialize(Snapshot(ns, env, version));

    public static Dictionary<string, FlagDefinition> DefaultFlags() => new()
    {
        ["new-checkout"] = new FlagDefinition
        {
            Type = FlagType.Boolean,
            Variants = new() { ["on"] = J("true"), ["off"] = J("false") },
            DefaultVariant = "off",
            Rules =
            [
                new TargetingRule
                {
                    Name = "beta tenants",
                    Conditions = [new Condition { Attribute = "tenant", Operator = ConditionOperator.In, Values = ["acme", "globex"] }],
                    Variant = "on",
                },
                new TargetingRule
                {
                    Name = "50% rollout",
                    Rollout = [new WeightedVariant { Variant = "on", Weight = 50 }, new WeightedVariant { Variant = "off", Weight = 50 }],
                },
            ],
            Metadata = new() { ["owner"] = "checkout-team" },
        },
        ["checkout-banner"] = new FlagDefinition
        {
            Type = FlagType.String,
            Variants = new() { ["none"] = J("\"\""), ["bf"] = J("\"Black Friday\"") },
            DefaultVariant = "bf",
        },
        ["max-items"] = new FlagDefinition
        {
            Type = FlagType.Integer,
            Variants = new() { ["small"] = J("10"), ["large"] = J("50") },
            DefaultVariant = "small",
        },
        ["legacy-flow"] = new FlagDefinition
        {
            Type = FlagType.Boolean,
            Enabled = false,
            Variants = new() { ["on"] = J("true"), ["off"] = J("false") },
            DefaultVariant = "off",
        },
    };
}

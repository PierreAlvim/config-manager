using System.Text.Json;
using ConfigManager.Contracts;

namespace ConfigManager.Management.Tests;

internal static class TestFlags
{
    public static FlagDefinition Bool(bool enabled = true, string defaultVariant = "off") => new()
    {
        Type = FlagType.Boolean,
        Enabled = enabled,
        Variants = new() { ["on"] = JsonSerializer.SerializeToElement(true), ["off"] = JsonSerializer.SerializeToElement(false) },
        DefaultVariant = defaultVariant,
    };

    public static FlagDefinition Str(string value = "x") => new()
    {
        Type = FlagType.String,
        Variants = new() { ["a"] = JsonSerializer.SerializeToElement(value) },
        DefaultVariant = "a",
    };
}

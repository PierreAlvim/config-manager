using OpenFeature.Model;

namespace ConfigManager.OpenFeature;

public static class ValueExtensions
{
    /// <summary>Converts an OpenFeature <see cref="Value"/> into plain CLR objects (dictionary / list / primitive) suitable for JSON serialization.</summary>
    public static object? ToPlainObject(this Value value)
    {
        if (value.IsNull) return null;
        if (value.IsStructure)
            return value.AsStructure!.AsDictionary().ToDictionary(kv => kv.Key, kv => kv.Value.ToPlainObject());
        if (value.IsList) return value.AsList!.Select(v => v.ToPlainObject()).ToList();
        return value.AsObject;
    }
}

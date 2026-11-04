using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConfigManager.Contracts;

/// <summary>Canonical JSON settings shared by every component that reads or writes snapshots.</summary>
public static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = Create();

    public static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        return o;
    }

    public static string Serialize(ConfigSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    public static ConfigSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<ConfigSnapshot>(json, Options)
        ?? throw new JsonException("Snapshot JSON was null");

    public static string Serialize(FlagDefinition flag) => JsonSerializer.Serialize(flag, Options);

    public static FlagDefinition DeserializeFlag(string json) =>
        JsonSerializer.Deserialize<FlagDefinition>(json, Options)
        ?? throw new JsonException("Flag JSON was null");
}

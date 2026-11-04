using System.Globalization;
using System.Text.Json;

namespace ConfigManager.Distribution.Api.Ofrep;

public static class ContextNormalizer
{
    /// <summary>
    /// Parses an OFREP request body <c>{ "context": { "targetingKey": "...", ... } }</c>. An empty body is an empty context.
    /// </summary>
    public static bool TryParseRequest(string? body, out EvaluationInput input, out string? error)
    {
        input = EvaluationInput.Empty;
        error = null;
        if (string.IsNullOrWhiteSpace(body)) return true;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Request body must be a JSON object.";
                return false;
            }
            if (!doc.RootElement.TryGetProperty("context", out var context) || context.ValueKind == JsonValueKind.Null)
                return true;
            if (context.ValueKind != JsonValueKind.Object)
            {
                error = "'context' must be a JSON object.";
                return false;
            }
            input = Normalize(context);
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Normalizes context values to strings: booleans to "true"/"false", numbers invariant, strings as-is;
    /// nulls, arrays and nested objects are skipped. <c>targetingKey</c> is kept apart.
    /// </summary>
    public static EvaluationInput Normalize(JsonElement context)
    {
        string? targetingKey = null;
        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var property in context.EnumerateObject())
        {
            var value = ToScalar(property.Value);
            if (value is null) continue;
            if (property.Name == EvaluationInput.TargetingKeyAttribute)
                targetingKey = value;
            else
                attributes[property.Name] = value;
        }
        return new EvaluationInput(targetingKey, attributes);
    }

    private static string? ToScalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.TryGetInt64(out var l)
            ? l.ToString(CultureInfo.InvariantCulture)
            : value.GetDouble().ToString("R", CultureInfo.InvariantCulture),
        _ => null,
    };
}

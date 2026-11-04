using System.Globalization;
using System.Text.Json;
using ConfigManager.Contracts;
using OpenFeature.Constant;
using OpenFeature.Model;

namespace ConfigManager.OpenFeature;

/// <summary>Conversions between OpenFeature types and the Config Manager evaluator contract.</summary>
internal static class EvaluationMapper
{
    public static EvaluationInput ToInput(EvaluationContext? context)
    {
        if (context is null || context.Count == 0) return EvaluationInput.Empty;

        var attributes = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in context.AsDictionary())
        {
            if (key == EvaluationInput.TargetingKeyAttribute) continue;
            var text = Normalize(value);
            if (text is not null) attributes[key] = text;
        }

        return new EvaluationInput(context.TargetingKey, attributes);
    }

    /// <summary>Attribute values are compared as strings by the evaluator; structures, lists and nulls are skipped.</summary>
    public static string? Normalize(Value value)
    {
        if (value.IsNull) return null;
        if (value.IsBoolean) return value.AsBoolean == true ? "true" : "false";
        if (value.IsString) return value.AsString;
        if (value.IsDateTime) return value.AsDateTime!.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        if (value.IsNumber)
        {
            return value.AsObject switch
            {
                IFormattable f => f.ToString(value.AsObject is double or float ? "R" : null, CultureInfo.InvariantCulture),
                var o => Convert.ToString(o, CultureInfo.InvariantCulture),
            };
        }

        return null;
    }

    public static bool TryBoolean(JsonElement e, out bool value)
    {
        value = e.ValueKind == JsonValueKind.True;
        return e.ValueKind is JsonValueKind.True or JsonValueKind.False;
    }

    public static bool TryString(JsonElement e, out string value)
    {
        value = e.ValueKind == JsonValueKind.String ? e.GetString()! : "";
        return e.ValueKind == JsonValueKind.String;
    }

    public static bool TryInteger(JsonElement e, out int value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out value);
    }

    public static bool TryDouble(JsonElement e, out double value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number && e.TryGetDouble(out value);
    }

    /// <summary>Only JSON objects and arrays are valid structure values.</summary>
    public static bool TryStructure(JsonElement e, out Value value)
    {
        value = new Value();
        if (e.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)) return false;
        value = ToValue(e);
        return true;
    }

    public static Value ToValue(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => new Value(true),
        JsonValueKind.False => new Value(false),
        JsonValueKind.String => new Value(e.GetString()!),
        JsonValueKind.Number => e.TryGetInt32(out var i) ? new Value(i) : new Value(e.GetDouble()),
        JsonValueKind.Object => new Value(new Structure(e.EnumerateObject().ToDictionary(p => p.Name, p => ToValue(p.Value)))),
        JsonValueKind.Array => new Value(e.EnumerateArray().Select(ToValue).ToList()),
        _ => new Value(),
    };

    public static string ToReason(EvaluationReason reason) => reason switch
    {
        EvaluationReason.Static => Reason.Static,
        EvaluationReason.Default => Reason.Default,
        EvaluationReason.TargetingMatch => Reason.TargetingMatch,
        EvaluationReason.Split => Reason.Split,
        EvaluationReason.Disabled => Reason.Disabled,
        _ => Reason.Error,
    };

    public static ErrorType ToErrorType(EvaluationErrorCode code) => code switch
    {
        EvaluationErrorCode.None => ErrorType.None,
        EvaluationErrorCode.FlagNotFound => ErrorType.FlagNotFound,
        EvaluationErrorCode.TypeMismatch => ErrorType.TypeMismatch,
        EvaluationErrorCode.ParseError => ErrorType.ParseError,
        _ => ErrorType.General,
    };
}

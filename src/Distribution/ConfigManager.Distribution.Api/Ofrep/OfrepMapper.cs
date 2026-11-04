using System.Security.Cryptography;
using System.Text;

namespace ConfigManager.Distribution.Api.Ofrep;

/// <summary>An OFREP evaluation outcome: HTTP status for single evaluation and the JSON body (also used as a bulk item).</summary>
public sealed record OfrepResult(int StatusCode, IReadOnlyDictionary<string, object?> Body);

public static class OfrepMapper
{
    public static string ReasonName(EvaluationReason reason) => reason switch
    {
        EvaluationReason.Static => "STATIC",
        EvaluationReason.Default => "DEFAULT",
        EvaluationReason.TargetingMatch => "TARGETING_MATCH",
        EvaluationReason.Split => "SPLIT",
        EvaluationReason.Disabled => "DISABLED",
        _ => "ERROR",
    };

    public static string ErrorCodeName(EvaluationErrorCode code) => code switch
    {
        EvaluationErrorCode.FlagNotFound => "FLAG_NOT_FOUND",
        EvaluationErrorCode.ParseError => "PARSE_ERROR",
        EvaluationErrorCode.TypeMismatch => "TYPE_MISMATCH",
        _ => "GENERAL",
    };

    public static int StatusFor(EvaluationErrorCode code) => code switch
    {
        EvaluationErrorCode.FlagNotFound => StatusCodes.Status404NotFound,
        EvaluationErrorCode.ParseError or EvaluationErrorCode.TypeMismatch => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };

    public static OfrepResult Map(string key, EvaluationResult result, long version, IReadOnlyDictionary<string, string>? flagMetadata = null)
    {
        if (result.Reason == EvaluationReason.Error || result.ErrorCode != EvaluationErrorCode.None)
        {
            var code = result.ErrorCode == EvaluationErrorCode.None ? EvaluationErrorCode.General : result.ErrorCode;
            return new OfrepResult(StatusFor(code), ErrorBody(key, ErrorCodeName(code), result.ErrorMessage));
        }

        var metadata = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (flagMetadata is not null)
            foreach (var (k, v) in flagMetadata) metadata[k] = v;
        metadata["version"] = version;

        var body = new Dictionary<string, object?>
        {
            ["key"] = key,
            // Disabled flags carry an explicit null so the client falls back to its own default.
            ["value"] = result.Value,
            ["reason"] = ReasonName(result.Reason),
        };
        if (result.Variant is not null) body["variant"] = result.Variant;
        body["metadata"] = metadata;
        return new OfrepResult(StatusCodes.Status200OK, body);
    }

    public static Dictionary<string, object?> ErrorBody(string? key, string errorCode, string? details)
    {
        var body = new Dictionary<string, object?>();
        if (key is not null) body["key"] = key;
        body["errorCode"] = errorCode;
        body["errorDetails"] = details;
        return body;
    }

    /// <summary>Bulk ETag: results depend on both the snapshot version and the evaluation context.</summary>
    public static string BulkETag(long version, EvaluationInput input)
    {
        var sb = new StringBuilder().Append(input.TargetingKey).Append('\0');
        foreach (var (k, v) in input.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal))
            sb.Append(k).Append('=').Append(v).Append('\0');
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
        return $"\"{version}-{hash}\"";
    }
}

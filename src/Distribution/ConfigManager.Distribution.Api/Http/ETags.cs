using Microsoft.Extensions.Primitives;

namespace ConfigManager.Distribution.Api.Http;

public static class ETags
{
    public static string ForVersion(long version) => $"\"{version}\"";

    /// <summary>True when an <c>If-None-Match</c> header value matches the strong ETag (weak prefix and <c>*</c> honoured).</summary>
    public static bool Matches(StringValues ifNoneMatch, string etag)
    {
        foreach (var header in ifNoneMatch)
        {
            foreach (var part in (header ?? "").Split(','))
            {
                var candidate = part.Trim();
                if (candidate == "*") return true;
                if (candidate.StartsWith("W/", StringComparison.Ordinal)) candidate = candidate[2..];
                if (candidate == etag) return true;
            }
        }
        return false;
    }
}

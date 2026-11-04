using System.Globalization;
using System.Text;

namespace ConfigManager.Distribution.Api.Http;

public static class SseFormatter
{
    public const string KeepAlive = ": keepalive\n\n";

    /// <summary>Formats a <c>snapshot</c> event. Newlines in the data (not expected from JSON) become extra data lines.</summary>
    public static string Snapshot(long version, string json)
    {
        var sb = new StringBuilder("event: snapshot\nid: ");
        sb.Append(version.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var line in json.ReplaceLineEndings("\n").Split('\n'))
            sb.Append("data: ").Append(line).Append('\n');
        return sb.Append('\n').ToString();
    }
}

using System.Text.Json;

namespace XafMcp.Module.Logs;

public sealed record ClefEvent(DateTimeOffset Timestamp, string Level, string Message, string? Exception, string? SourceContext);

public static class ClefParser {
    /// <summary>One CLEF (Compact Log Event Format) line → event, or null when malformed.
    /// "@m" is preferred; "@mt" (message template, un-rendered) is the usual CLEF field.</summary>
    public static ClefEvent? ParseLine(string line) {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("@t", out var t) || !t.TryGetDateTimeOffset(out var timestamp)) return null;
            var level = root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information";
            var message = root.TryGetProperty("@m", out var m) ? m.GetString()
                : root.TryGetProperty("@mt", out var mt) ? mt.GetString() : null;
            var exception = root.TryGetProperty("@x", out var x) ? x.GetString() : null;
            var source = root.TryGetProperty("SourceContext", out var sc) ? sc.GetString() : null;
            return new ClefEvent(timestamp, level, message ?? "", exception, source);
        }
        catch (JsonException) { return null; }
    }

    public static IEnumerable<ClefEvent> ParseFiles(IEnumerable<string> paths) {
        foreach (var path in paths.OrderBy(p => p)) {
            // FileShare.ReadWrite: Serilog holds the current file open (shared: true)
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null) {
                var parsed = ParseLine(line);
                if (parsed != null) yield return parsed;
            }
        }
    }
}

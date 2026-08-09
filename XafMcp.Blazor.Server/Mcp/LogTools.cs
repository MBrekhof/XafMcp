using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using XafMcp.Module.Logs;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class LogTools(IWebHostEnvironment environment) {
    static readonly string[] LevelOrder = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    IEnumerable<ClefEvent> Load(DateTime? from, DateTime? to) {
        var dir = Path.Combine(environment.ContentRootPath, "logs");
        if (!Directory.Exists(dir)) return [];
        var files = Directory.GetFiles(dir, "xafmcp-*.clef");
        var events = ClefParser.ParseFiles(files);
        if (from.HasValue) events = events.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue) events = events.Where(e => e.Timestamp <= to.Value);
        return events;
    }

    [McpServerTool(Name = "search_logs")]
    [Description("Search the application's Serilog CLEF files. Times are ISO 8601 (e.g. 2026-08-01T00:00:00Z). min_level: Verbose|Debug|Information|Warning|Error|Fatal.")]
    public string SearchLogs(
        [Description("Only events at/after this time")] DateTime? from = null,
        [Description("Only events at/before this time")] DateTime? to = null,
        [Description("Minimum level, default Information")] string min_level = "Information",
        [Description("Substring to match in message/exception/source")] string? contains = null,
        [Description(".NET regex to match instead of substring")] string? regex = null,
        [Description("Max events, default 100, cap 500")] int top = 100) {
        top = Math.Clamp(top, 1, 500);
        int minIndex = Array.FindIndex(LevelOrder, l => l.Equals(min_level, StringComparison.OrdinalIgnoreCase));
        if (minIndex < 0) throw new ModelContextProtocol.McpException($"Unknown level '{min_level}'. Valid: {string.Join("|", LevelOrder)}");
        Regex? rx = null;
        if (regex != null) {
            try { rx = new Regex(regex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex) { throw new ModelContextProtocol.McpException($"Invalid regex: {ex.Message}"); }
        }
        bool Matches(ClefEvent e) {
            var haystack = $"{e.Message}\n{e.Exception}\n{e.SourceContext}";
            if (contains != null && !haystack.Contains(contains, StringComparison.OrdinalIgnoreCase)) return false;
            if (rx != null && !rx.IsMatch(haystack)) return false;
            return true;
        }
        var events = Load(from, to)
            .Where(e => Array.IndexOf(LevelOrder, e.Level) >= minIndex && Matches(e))
            .OrderByDescending(e => e.Timestamp)
            .Take(top)
            .Select(e => new {
                timestamp = e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                level = e.Level,
                message = e.Message,
                exception = e.Exception,
                source = e.SourceContext,
            })
            .ToList();
        return JsonSerializer.Serialize(new { returned = events.Count, events }, JsonOpts.Indented);
    }

    [McpServerTool(Name = "summarize_logs")]
    [Description("Aggregate view of the log files: counts per level, top exception types, top sources. Start here for 'any errors lately?'.")]
    public string SummarizeLogs(
        [Description("Only events at/after this time")] DateTime? from = null,
        [Description("Only events at/before this time")] DateTime? to = null) {
        var events = Load(from, to).ToList();
        var byLevel = events.GroupBy(e => e.Level).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var topExceptions = events.Where(e => !string.IsNullOrEmpty(e.Exception))
            .GroupBy(e => e.Exception!.Split(':')[0].Split('\n')[0].Trim())
            .OrderByDescending(g => g.Count()).Take(5)
            .Select(g => new { exceptionType = g.Key, count = g.Count() }).ToList();
        var topSources = events.Where(e => e.SourceContext != null)
            .GroupBy(e => e.SourceContext!)
            .OrderByDescending(g => g.Count()).Take(5)
            .Select(g => new { source = g.Key, count = g.Count() }).ToList();
        return JsonSerializer.Serialize(new {
            totalEvents = events.Count,
            firstEvent = events.Count > 0 ? events.Min(e => e.Timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") : null,
            lastEvent = events.Count > 0 ? events.Max(e => e.Timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") : null,
            byLevel, topExceptions, topSources,
        }, JsonOpts.Indented);
    }
}

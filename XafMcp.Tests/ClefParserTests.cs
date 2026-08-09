using XafMcp.Module.Logs;

namespace XafMcp.Tests;

[TestClass]
public sealed class ClefParserTests {
    [TestMethod]
    public void Parses_full_event() {
        var line = """{"@t":"2026-08-09T10:15:30.1234567Z","@l":"Error","@mt":"Boom {Name}","@x":"System.InvalidOperationException: nope","SourceContext":"XafMcp.Startup","Name":"x"}""";
        var e = ClefParser.ParseLine(line);
        Assert.IsNotNull(e);
        Assert.IsTrue(e.Level == "Error");
        Assert.IsTrue(e.Message.Contains("Boom"));
        Assert.IsTrue(e.Exception!.Contains("InvalidOperationException"));
        Assert.IsTrue(e.SourceContext == "XafMcp.Startup");
        Assert.IsTrue(e.Timestamp.UtcDateTime.Hour == 10);
    }

    [TestMethod]
    public void Level_defaults_to_Information() {
        var e = ClefParser.ParseLine("""{"@t":"2026-08-09T10:15:30Z","@mt":"hello"}""");
        Assert.IsNotNull(e);
        Assert.IsTrue(e.Level == "Information");
    }

    [TestMethod]
    public void Malformed_lines_yield_null() {
        Assert.IsNull(ClefParser.ParseLine("not json at all"));
        Assert.IsNull(ClefParser.ParseLine("""{"no_timestamp":true}"""));
        Assert.IsNull(ClefParser.ParseLine(""));
    }

    [TestMethod]
    public void ParseFiles_skips_malformed_lines() {
        var tmp = Path.Combine(Path.GetTempPath(), $"clef-{Guid.NewGuid():N}.json");
        File.WriteAllLines(tmp, [
            """{"@t":"2026-08-09T10:00:00Z","@mt":"one"}""",
            "garbage",
            """{"@t":"2026-08-09T11:00:00Z","@l":"Warning","@mt":"two"}""",
        ]);
        try {
            var events = ClefParser.ParseFiles([tmp]).ToList();
            Assert.HasCount(2, events);
            Assert.IsTrue(events[1].Level == "Warning");
        } finally { File.Delete(tmp); }
    }
}

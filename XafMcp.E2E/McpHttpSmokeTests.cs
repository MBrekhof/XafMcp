using System.Text;
using System.Text.Json;

namespace XafMcp.E2E;

[TestFixture]
public class McpHttpSmokeTests {
    const string McpUrl = "http://localhost:5210/mcp";
    static readonly string[] ExpectedTools = [
        "get_server_info", "list_entities", "describe_entity", "query_entities", "aggregate_entities",
        "list_roles", "explain_permissions", "check_schema_drift", "search_logs", "summarize_logs",
    ];

    HttpClient http = null!;
    string? sessionId;

    [SetUp]
    public void SetUp() {
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
    }

    [TearDown]
    public void TearDown() => http.Dispose();

    async Task<JsonElement?> Post(object payload, bool expectBody = true) {
        var request = new HttpRequestMessage(HttpMethod.Post, McpUrl) {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        if (sessionId != null) request.Headers.Add("mcp-session-id", sessionId);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        catch (HttpRequestException) { Assert.Ignore("App not running — start it with scripts/run-app.ps1 first."); return null; }
        if (response.Headers.TryGetValues("mcp-session-id", out var values)) sessionId = values.First();
        if (!expectBody) return null;
        var body = await response.Content.ReadAsStringAsync();
        var dataLine = body.Split('\n').FirstOrDefault(l => l.StartsWith("data: "));
        var json = dataLine != null ? dataLine["data: ".Length..] : body;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    async Task Initialize() {
        await Post(new {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "e2e", version = "1.0" } },
        });
        await Post(new { jsonrpc = "2.0", method = "notifications/initialized" }, expectBody: false);
    }

    [Test]
    public async Task All_ten_tools_are_listed() {
        await Initialize();
        var response = await Post(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        var names = response!.Value.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.That(names, Is.SupersetOf(ExpectedTools));
    }

    [Test]
    public async Task Query_returns_rows_and_never_hourly_rate() {
        await Initialize();
        var response = await Post(new {
            jsonrpc = "2.0", id = 2, method = "tools/call",
            @params = new { name = "query_entities", arguments = new { entity = "Person", top = 5 } },
        });
        var text = response!.Value.GetProperty("result").GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!;
        Assert.That(text, Does.Contain("FirstName"));
        Assert.That(text, Does.Not.Contain("HourlyRate"));
    }
}

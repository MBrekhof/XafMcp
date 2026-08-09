using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class ServerInfoTools(IConfiguration configuration) {
    static readonly DateTime StartedUtc = DateTime.UtcNow;

    [McpServerTool(Name = "get_server_info")]
    [Description("Application, DevExpress and .NET versions, uptime, database name, MCP identity and tool limits.")]
    public string GetServerInfo() {
        var cs = configuration.GetConnectionString("ConnectionString") ?? "";
        var info = new {
            application = "XafMcp",
            appVersion = typeof(ServerInfoTools).Assembly.GetName().Version?.ToString(),
            devExpressVersion = typeof(DevExpress.ExpressApp.XafApplication).Assembly.GetName().Version?.ToString(),
            dotnetVersion = Environment.Version.ToString(),
            uptime = (DateTime.UtcNow - StartedUtc).ToString(@"d\.hh\:mm\:ss"),
            database = new SqlConnectionStringBuilder(cs).InitialCatalog,
            mcpIdentity = "McpAgent",
            limits = new { queryTopMax = 500, aggregateGroupMax = 500 },
        };
        return JsonSerializer.Serialize(info, JsonOpts.Indented);
    }
}

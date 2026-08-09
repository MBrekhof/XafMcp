using System.Text.Json;

namespace XafMcp.Blazor.Server.Mcp;

public static class JsonOpts {
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}

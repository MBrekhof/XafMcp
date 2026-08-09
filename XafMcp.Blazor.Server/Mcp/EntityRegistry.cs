using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using ModelContextProtocol;
using XafMcp.Module.BusinessObjects;

namespace XafMcp.Blazor.Server.Mcp;

public static class EntityRegistry {
    // Whitelist: MCP exposes exactly the 8 domain entities — never security/system types.
    public static readonly Type[] DomainTypes = [
        typeof(Region), typeof(Customer), typeof(Person), typeof(Product),
        typeof(Order), typeof(OrderLine), typeof(Project), typeof(ProjectTask),
    ];

    public static ITypeInfo Resolve(string entity) {
        var type = DomainTypes.FirstOrDefault(t =>
            t.Name.Equals(entity, StringComparison.OrdinalIgnoreCase) ||
            t.FullName!.Equals(entity, StringComparison.OrdinalIgnoreCase));
        if (type == null) {
            throw new McpException($"Unknown entity '{entity}'. Valid entities: {string.Join(", ", DomainTypes.Select(t => t.Name))}");
        }
        return XafTypesInfo.Instance.FindTypeInfo(type);
    }
}

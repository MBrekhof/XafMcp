using System.ComponentModel;
using System.Text.Json;
using DevExpress.ExpressApp.DC;
using ModelContextProtocol.Server;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class DataTools(McpSecurityContext securityContext) {
    [McpServerTool(Name = "list_entities")]
    [Description("List the queryable business entities of this application. Start here; then use describe_entity for properties.")]
    public string ListEntities() {
        var entities = EntityRegistry.DomainTypes.Select(t => {
            var ti = EntityRegistry.Resolve(t.Name);
            return new { name = t.Name, caption = ti.Type.Name, fullName = t.FullName };
        });
        return JsonSerializer.Serialize(new { entities }, JsonOpts.Indented);
    }

    [McpServerTool(Name = "describe_entity")]
    [Description("Property metadata for one entity: names, types, enum values, reference targets, key. Property names returned here are exactly what query_entities/aggregate_entities criteria and paths accept.")]
    public string DescribeEntity(
        [Description("Entity name from list_entities, e.g. 'Customer'")] string entity) {
        var ti = EntityRegistry.Resolve(entity);
        var properties = ti.Members
            .Where(m => m.IsPublic && m.IsPersistent)
            .Where(m => m.Name != "ID" && !m.Name.EndsWith("Id") && !m.Name.Contains("GCRecord") && !m.Name.Contains("OptimisticLockField"))
            .Select(m => new {
                name = m.Name,
                type = m.MemberType.IsEnum ? "enum" : SimpleTypeName(m.MemberType),
                enumValues = m.MemberType.IsEnum ? Enum.GetNames(m.MemberType) : null,
                isCollection = m.IsList,
                referencesEntity = !m.IsList && m.MemberTypeInfo?.IsPersistent == true ? m.MemberTypeInfo.Type.Name : null,
                nullable = m.MemberType.IsClass || Nullable.GetUnderlyingType(m.MemberType) != null,
            });
        return JsonSerializer.Serialize(new {
            entity = ti.Type.Name,
            key = ti.KeyMember?.Name,
            properties,
        }, JsonOpts.Indented);
    }

    [McpServerTool(Name = "query_entities")]
    [Description("Query one entity through the secured object space. criteria uses XAF criteria syntax, e.g. \"Status = 'Shipped' And OrderDate > #2026-01-01#\" or \"Customer.Region.Name = 'North'\". Use describe_entity for valid property names.")]
    public string QueryEntities(
        [Description("Entity name from list_entities")] string entity,
        [Description("XAF criteria string; omit for all rows")] string? criteria = null,
        [Description("Property name to sort by")] string? sort = null,
        [Description("Sort descending")] bool sortDescending = false,
        [Description("Max rows, default 50, cap 500")] int top = 50,
        [Description("Restrict output to these properties")] string[]? properties = null) {
        var ti = EntityRegistry.Resolve(entity);
        top = Math.Clamp(top, 1, 500);
        DevExpress.Data.Filtering.CriteriaOperator? crit = null;
        if (!string.IsNullOrWhiteSpace(criteria)) {
            try { crit = DevExpress.Data.Filtering.CriteriaOperator.Parse(criteria); }
            catch (Exception ex) {
                throw new ModelContextProtocol.McpException(
                    $"Invalid criteria: {ex.Message}. XAF criteria syntax examples: \"Status = 'Shipped'\", \"OrderDate > #2026-01-01#\", \"Customer.Region.Name = 'North'\". Call describe_entity('{entity}') for valid property names.");
            }
        }
        var sorting = new List<DevExpress.Xpo.SortProperty>();
        if (!string.IsNullOrWhiteSpace(sort)) {
            sorting.Add(new DevExpress.Xpo.SortProperty(sort,
                sortDescending ? DevExpress.Xpo.DB.SortingDirection.Descending : DevExpress.Xpo.DB.SortingDirection.Ascending));
        }
        using var os = securityContext.CreateObjectSpace(ti.Type);
        var denied = PermissionInspector.GetDeniedReadMembers(os, McpSecurityContext.RoleName, ti.Type);
        var list = ((DevExpress.ExpressApp.EFCore.EFCoreObjectSpace)os).GetObjects(ti.Type, crit, sorting, false);
        // ponytail: top is applied after materialization — fine at POC row counts (≤500 orders);
        // switch to GetObjectsQuery<T> via MakeGenericMethod for server-side Take if datasets grow
        var rows = list.Cast<object>().Take(top)
            .Select(o => EntityProjector.Project(o, ti, denied, properties)).ToList();
        return System.Text.Json.JsonSerializer.Serialize(new { entity = ti.Type.Name, returned = rows.Count, rows }, JsonOpts.Indented);
    }

    static string SimpleTypeName(Type t) {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsGenericType) return t.Name; // collections keep their generic name
        return t.Name switch {
            "String" => "string", "Int32" => "int", "Decimal" => "decimal",
            "Boolean" => "bool", "DateTime" => "datetime", _ => t.Name,
        };
    }
}

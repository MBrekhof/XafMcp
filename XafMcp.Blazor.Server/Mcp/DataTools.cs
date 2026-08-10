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
        List<Dictionary<string, object?>> rows;
        try {
            var list = ((DevExpress.ExpressApp.EFCore.EFCoreObjectSpace)os).GetObjects(ti.Type, crit, sorting, false);
            // ponytail: top is applied after materialization — fine at POC row counts (≤500 orders);
            // switch to GetObjectsQuery<T> via MakeGenericMethod for server-side Take if datasets grow
            rows = list.Cast<object>().Take(top)
                .Select(o => EntityProjector.Project(o, ti, denied, properties)).ToList();
        }
        catch (Exception ex) when (ex is not ModelContextProtocol.McpException) {
            // Criteria that parse but fail at conversion/materialization (e.g. enum <> 'string') land here.
            throw new ModelContextProtocol.McpException(
                $"Query failed: {XafMcp.Module.Services.CriteriaErrorHelper.Describe(ex)}");
        }
        return System.Text.Json.JsonSerializer.Serialize(new { entity = ti.Type.Name, returned = rows.Count, rows }, JsonOpts.Indented);
    }

    [McpServerTool(Name = "aggregate_entities")]
    [Description("Group-and-aggregate one entity. group_by accepts property paths like 'Customer.Region.Name' or 'Status'. function: count | sum | avg | min | max (sum/avg/min/max need measure). Optional XAF criteria pre-filters rows.")]
    public string AggregateEntities(
        [Description("Entity name from list_entities")] string entity,
        [Description("Group-by property path, e.g. 'Customer.Region.Name'")] string group_by,
        [Description("count | sum | avg | min | max")] string function = "count",
        [Description("Numeric property path to measure (required for sum/avg/min/max)")] string? measure = null,
        [Description("XAF criteria string to pre-filter rows")] string? criteria = null) {
        var ti = EntityRegistry.Resolve(entity);
        function = function.ToLowerInvariant();
        if (function is not ("count" or "sum" or "avg" or "min" or "max")) {
            throw new ModelContextProtocol.McpException("function must be one of: count, sum, avg, min, max");
        }
        if (function != "count" && string.IsNullOrWhiteSpace(measure)) {
            throw new ModelContextProtocol.McpException($"function '{function}' requires a measure property");
        }
        DevExpress.Data.Filtering.CriteriaOperator? crit = null;
        if (!string.IsNullOrWhiteSpace(criteria)) {
            try { crit = DevExpress.Data.Filtering.CriteriaOperator.Parse(criteria); }
            catch (Exception ex) { throw new ModelContextProtocol.McpException($"Invalid criteria: {ex.Message}"); }
        }
        using var os = securityContext.CreateObjectSpace(ti.Type);
        GuardPathAgainstDeniedMembers(os, ti, group_by);
        if (measure != null) GuardPathAgainstDeniedMembers(os, ti, measure);

        try {
            var list = ((DevExpress.ExpressApp.EFCore.EFCoreObjectSpace)os).GetObjects(ti.Type, crit, new List<DevExpress.Xpo.SortProperty>(), false);
            // ponytail: in-memory grouping — correct and simple at POC scale; move to a LINQ GroupBy over
            // GetObjectsQuery<T> if row counts grow past a few thousand
            var groups = list.Cast<object>()
                .GroupBy(o => XafMcp.Module.Services.PathValueResolver.GetValue(o, group_by)?.ToString() ?? "(null)")
                .Select(g => new {
                    group = g.Key,
                    count = g.Count(),
                    value = function switch {
                        "count" => (decimal?)g.Count(),
                        "sum" => g.Sum(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                        "avg" => g.Average(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                        "min" => g.Min(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                        _ => g.Max(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                    },
                })
                .OrderByDescending(g => g.value)
                .Take(500)
                .ToList();
            return JsonSerializer.Serialize(new { entity = ti.Type.Name, group_by, function, measure, groups }, JsonOpts.Indented);
        }
        catch (Exception ex) when (ex is not ModelContextProtocol.McpException) {
            // Criteria that parse but fail at conversion/materialization (e.g. enum <> 'string') land here.
            throw new ModelContextProtocol.McpException(
                $"Aggregate failed: {XafMcp.Module.Services.CriteriaErrorHelper.Describe(ex)}");
        }
    }

    static decimal ToDecimal(object? value) => value is null ? 0m : Convert.ToDecimal(value);

    void GuardPathAgainstDeniedMembers(DevExpress.ExpressApp.IObjectSpace os, ITypeInfo rootTi, string path) {
        // Walk the ITypeInfo chain alongside the path; refuse any segment the MCP role can't read.
        var currentTi = rootTi;
        foreach (var segment in path.Split('.')) {
            if (currentTi == null) break;
            var denied = PermissionInspector.GetDeniedReadMembers(os, McpSecurityContext.RoleName, currentTi.Type);
            if (denied.Contains(segment)) {
                throw new ModelContextProtocol.McpException($"Access to '{currentTi.Type.Name}.{segment}' is denied for the MCP role.");
            }
            currentTi = currentTi.FindMember(segment)?.MemberTypeInfo;
        }
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

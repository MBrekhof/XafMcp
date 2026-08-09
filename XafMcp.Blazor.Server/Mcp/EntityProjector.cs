using DevExpress.ExpressApp.DC;

namespace XafMcp.Blazor.Server.Mcp;

public static class EntityProjector {
    /// <summary>
    /// Projects an entity to a plain dictionary via IMemberInfo (never serialize EF proxies).
    /// Persistent scalar members + reference display text; denied members are omitted entirely.
    /// </summary>
    public static Dictionary<string, object?> Project(object obj, ITypeInfo ti, ISet<string> deniedMembers, string[]? properties) {
        var row = new Dictionary<string, object?>();
        foreach (var m in ti.Members) {
            if (!m.IsPublic || m.IsList || !m.IsPersistent) continue;
            if (m.Name.Contains("GCRecord") || m.Name == "OptimisticLockField") continue;
            if (m.Name.EndsWith("Id") && ti.FindMember(m.Name[..^2]) != null) continue; // hide FK scalar when the reference member exists
            if (deniedMembers.Contains(m.Name)) continue;
            if (properties is { Length: > 0 } && !properties.Contains(m.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var value = m.GetValue(obj);
            row[m.Name] = value switch {
                null => null,
                Enum e => e.ToString(),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                decimal d => d,
                _ when m.MemberTypeInfo?.IsPersistent == true => DisplayText(value, m.MemberTypeInfo),
                _ => value,
            };
        }
        return row;
    }

    static string? DisplayText(object referenced, ITypeInfo refTi) {
        var displayMember = refTi.DefaultMember ?? refTi.KeyMember;
        return displayMember?.GetValue(referenced)?.ToString();
    }
}

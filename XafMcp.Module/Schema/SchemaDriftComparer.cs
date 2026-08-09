namespace XafMcp.Module.Schema;

public sealed record ColumnInfo(string Table, string Column, string StoreType, bool IsNullable);
public sealed record DriftFinding(string Kind, string Table, string? Column, string? ModelValue, string? DbValue, string Severity);

public static class SchemaDriftComparer {
    /// <summary>Pure comparison of EF relational model columns vs INFORMATION_SCHEMA columns.
    /// "Model missing in DB" is an error (EF will fail at runtime); the rest are warnings.</summary>
    public static List<DriftFinding> Compare(IReadOnlyList<ColumnInfo> model, IReadOnlyList<ColumnInfo> db) {
        var findings = new List<DriftFinding>();
        var cmp = StringComparer.OrdinalIgnoreCase;
        var modelTables = model.Select(c => c.Table).Distinct(cmp).ToHashSet(cmp);
        var dbTables = db.Select(c => c.Table).Distinct(cmp).ToHashSet(cmp);

        foreach (var table in dbTables.Where(t => !modelTables.Contains(t)).OrderBy(t => t, cmp)) {
            findings.Add(new DriftFinding("table_only_in_db", table, null, null, null, "warning"));
        }

        var dbByKey = db.ToDictionary(c => (c.Table.ToLowerInvariant(), c.Column.ToLowerInvariant()));
        var modelByKey = model.ToDictionary(c => (c.Table.ToLowerInvariant(), c.Column.ToLowerInvariant()));

        foreach (var m in model) {
            if (!dbByKey.TryGetValue((m.Table.ToLowerInvariant(), m.Column.ToLowerInvariant()), out var d)) {
                findings.Add(new DriftFinding("column_missing_in_db", m.Table, m.Column, m.StoreType, null, "error"));
                continue;
            }
            if (!string.Equals(Normalize(m.StoreType), Normalize(d.StoreType), StringComparison.OrdinalIgnoreCase)) {
                findings.Add(new DriftFinding("type_mismatch", m.Table, m.Column, m.StoreType, d.StoreType, "warning"));
            }
            if (m.IsNullable != d.IsNullable) {
                findings.Add(new DriftFinding("nullability_mismatch", m.Table, m.Column,
                    m.IsNullable ? "NULL" : "NOT NULL", d.IsNullable ? "NULL" : "NOT NULL", "warning"));
            }
        }

        foreach (var d in db) {
            if (!dbTables.Contains(d.Table) || !modelTables.Contains(d.Table)) continue;
            if (!modelByKey.ContainsKey((d.Table.ToLowerInvariant(), d.Column.ToLowerInvariant()))) {
                findings.Add(new DriftFinding("column_only_in_db", d.Table, d.Column, null, d.StoreType, "warning"));
            }
        }
        return findings;
    }

    static string Normalize(string storeType) => storeType.Replace(" ", "").ToLowerInvariant();
}

using XafMcp.Module.Schema;

namespace XafMcp.Tests;

[TestClass]
public sealed class SchemaDriftComparerTests {
    static ColumnInfo Col(string table, string column, string type, bool nullable = false) => new(table, column, type, nullable);

    [TestMethod]
    public void Identical_schemas_produce_no_findings() {
        var cols = new[] { Col("Customers", "Name", "nvarchar(100)") };
        Assert.IsEmpty(SchemaDriftComparer.Compare(cols, cols));
    }

    [TestMethod]
    public void Extra_db_column_is_reported() {
        var model = new[] { Col("Customers", "Name", "nvarchar(100)") };
        var db = new[] { Col("Customers", "Name", "nvarchar(100)"), Col("Customers", "LegacyCode", "nvarchar(20)", true) };
        var findings = SchemaDriftComparer.Compare(model, db);
        Assert.HasCount(1, findings);
        Assert.IsTrue(findings[0].Kind == "column_only_in_db" && findings[0].Column == "LegacyCode");
    }

    [TestMethod]
    public void Missing_db_column_is_reported_as_error() {
        var model = new[] { Col("Customers", "Name", "nvarchar(100)"), Col("Customers", "City", "nvarchar(100)") };
        var db = new[] { Col("Customers", "Name", "nvarchar(100)") };
        var findings = SchemaDriftComparer.Compare(model, db);
        Assert.HasCount(1, findings);
        Assert.IsTrue(findings[0].Kind == "column_missing_in_db" && findings[0].Severity == "error");
    }

    [TestMethod]
    public void Type_and_nullability_mismatches_are_both_reported() {
        var model = new[] { Col("Regions", "Name", "nvarchar(100)", nullable: false) };
        var db = new[] { Col("Regions", "Name", "nvarchar(200)", nullable: true) };
        var findings = SchemaDriftComparer.Compare(model, db);
        Assert.HasCount(2, findings);
        Assert.IsTrue(findings.Any(f => f.Kind == "type_mismatch"));
        Assert.IsTrue(findings.Any(f => f.Kind == "nullability_mismatch"));
    }

    [TestMethod]
    public void Unknown_db_table_is_reported_once() {
        var model = new[] { Col("Customers", "Name", "nvarchar(100)") };
        var db = new[] { Col("Customers", "Name", "nvarchar(100)"), Col("Legacy", "A", "int"), Col("Legacy", "B", "int") };
        var findings = SchemaDriftComparer.Compare(model, db);
        Assert.HasCount(1, findings);
        Assert.IsTrue(findings[0].Kind == "table_only_in_db" && findings[0].Table == "Legacy");
    }

    [TestMethod]
    public void Comparison_is_case_insensitive_on_types() {
        var model = new[] { Col("Orders", "Total", "decimal(19,4)") };
        var db = new[] { Col("Orders", "Total", "DECIMAL(19,4)") };
        Assert.IsEmpty(SchemaDriftComparer.Compare(model, db));
    }
}

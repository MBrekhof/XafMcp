using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using XafMcp.Module.BusinessObjects;

namespace XafMcp.Tests;

[TestClass]
public sealed class ModelSmokeTests {
    static XafMcpEFCoreDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<XafMcpEFCoreDbContext>()
            .UseSqlServer("Data Source=(localdb)\\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafMcpModelOnly")
            .UseChangeTrackingProxies() // deviation: OnModelCreating requires notification change tracking; without proxies, model validation fails even on Task 1's pre-existing ModelDifference entity
            .Options;
        return new XafMcpEFCoreDbContext(options); // model builds without connecting
    }

    [TestMethod]
    public void Model_contains_all_domain_tables() {
        using var ctx = CreateContext();
        var tables = ctx.Model.GetRelationalModel().Tables.Select(t => t.Name).ToList();
        foreach (var expected in new[] { "Regions", "Customers", "Persons", "Products", "Orders", "OrderLines", "Projects", "ProjectTasks" }) {
            Assert.IsTrue(tables.Contains(expected), $"missing table {expected}");
        }
    }

    [TestMethod]
    public void Decimal_columns_have_explicit_precision() {
        using var ctx = CreateContext();
        var relational = ctx.Model.GetRelationalModel();
        var checks = new (string Table, string Column)[] {
            ("Persons", "HourlyRate"), ("Products", "UnitPrice"), ("Orders", "Total"),
            ("OrderLines", "UnitPrice"), ("OrderLines", "Discount"), ("Projects", "Budget"),
        };
        foreach (var (table, column) in checks) {
            var col = relational.Tables.Single(t => t.Name == table).Columns.Single(c => c.Name == column);
            Assert.IsTrue(col.StoreType == "decimal(19,4)", $"{table}.{column} is {col.StoreType}, expected decimal(19,4)");
        }
    }
}

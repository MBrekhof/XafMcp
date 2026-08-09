using XafMcp.Module.BusinessObjects;
using XafMcp.Module.DemoData;

namespace XafMcp.Tests;

[TestClass]
public sealed class DemoDataGeneratorTests {
    static readonly DateTime Anchor = new(2026, 8, 1);

    [TestMethod]
    public void Generation_is_deterministic() {
        var a = DemoDataGenerator.Generate(Anchor);
        var b = DemoDataGenerator.Generate(Anchor);
        Assert.IsTrue(a.Orders.Count == b.Orders.Count);
        Assert.IsTrue(a.Orders[0].OrderDate == b.Orders[0].OrderDate);
        Assert.IsTrue(a.Orders[^1].Lines.Count == b.Orders[^1].Lines.Count);
        Assert.IsTrue(a.Persons[3].HourlyRate == b.Persons[3].HourlyRate);
    }

    [TestMethod]
    public void Counts_match_spec() {
        var d = DemoDataGenerator.Generate(Anchor);
        Assert.HasCount(5, d.Regions);
        Assert.HasCount(30, d.Customers);
        Assert.HasCount(20, d.Products);
        Assert.HasCount(500, d.Orders);
        Assert.HasCount(6, d.Projects);
        Assert.IsTrue(d.ProjectTasks.Count >= 55 && d.ProjectTasks.Count <= 65, $"tasks: {d.ProjectTasks.Count}");
        Assert.IsTrue(d.Persons.Count >= 10);
    }

    [TestMethod]
    public void Orders_span_trailing_24_months() {
        var d = DemoDataGenerator.Generate(Anchor);
        Assert.IsTrue(d.Orders.All(o => o.OrderDate >= Anchor.AddMonths(-24) && o.OrderDate <= Anchor));
        // both halves of the window are populated
        Assert.IsTrue(d.Orders.Any(o => o.OrderDate < Anchor.AddMonths(-12)));
        Assert.IsTrue(d.Orders.Any(o => o.OrderDate >= Anchor.AddMonths(-3)));
    }

    [TestMethod]
    public void North_overindexes_on_software() {
        var d = DemoDataGenerator.Generate(Anchor);
        double Share(IEnumerable<DemoDataGenerator.OrderSeed> orders) {
            var lines = orders.SelectMany(o => o.Lines).ToList();
            return lines.Count == 0 ? 0 : (double)lines.Count(l => d.Products[l.ProductIndex].Category == ProductCategory.Software) / lines.Count;
        }
        var northCustomers = d.Customers.Select((c, i) => (c, i)).Where(x => x.c.Region == "North").Select(x => x.i).ToHashSet();
        var northShare = Share(d.Orders.Where(o => northCustomers.Contains(o.CustomerIndex)));
        var overallShare = Share(d.Orders);
        Assert.IsTrue(northShare > overallShare + 0.05, $"north {northShare:F2} vs overall {overallShare:F2}");
    }
}

using XafMcp.Module.BusinessObjects;

namespace XafMcp.Module.DemoData;

/// <summary>
/// Pure, deterministic demo-data generator (spec: "Demo data"). No ObjectSpace, no DB —
/// the Updater (Task 4) persists what this returns. Regional bias is deliberate so
/// "opportunity by region" reports find real signal.
/// </summary>
public static class DemoDataGenerator {
    public sealed record RegionSeed(string Name);
    public sealed record CustomerSeed(string Name, string Region, string City);
    public sealed record PersonSeed(string First, string Last, string Email, string Phone, decimal HourlyRate);
    public sealed record ProductSeed(string Name, ProductCategory Category, decimal UnitPrice);
    public sealed record LineSeed(int ProductIndex, int Quantity, decimal Discount);
    public sealed record OrderSeed(int CustomerIndex, DateTime OrderDate, OrderStatus Status, List<LineSeed> Lines);
    public sealed record ProjectSeed(string Name, int CustomerIndex, int ManagerIndex, DateTime StartDate, DateTime? DueDate, ProjectStatus Status, decimal Budget);
    public sealed record TaskSeed(string Subject, int ProjectIndex, int PersonIndex, ProjectTaskStatus Status, DateTime? DueDate, decimal EstimatedHours, decimal ActualHours);
    public sealed record DemoSet(
        List<RegionSeed> Regions, List<CustomerSeed> Customers, List<PersonSeed> Persons,
        List<ProductSeed> Products, List<OrderSeed> Orders, List<ProjectSeed> Projects,
        List<TaskSeed> ProjectTasks);

    static readonly string[] RegionNames = ["North", "South", "East", "West", "Central"];
    static readonly string[] Cities = ["Amsterdam", "Rotterdam", "Utrecht", "Eindhoven", "Groningen", "Arnhem", "Zwolle", "Breda"];
    static readonly string[] FirstNames = ["Anna", "Bram", "Carla", "Daan", "Eva", "Frank", "Gijs", "Hanna", "Iris", "Jan", "Kim", "Lars"];
    static readonly string[] LastNames = ["deVries", "Jansen", "Bakker", "Visser", "Smit", "Meijer", "Mulder", "Bos", "Peters", "Hendriks", "Dekker", "Kok"];

    public static DemoSet Generate(DateTime anchorDate, int seed = 42) {
        var rnd = new Random(seed);

        var regions = RegionNames.Select(n => new RegionSeed(n)).ToList();

        var persons = Enumerable.Range(0, 12).Select(i => new PersonSeed(
            FirstNames[i], LastNames[i],
            $"{FirstNames[i].ToLowerInvariant()}.{LastNames[i].ToLowerInvariant()}@xafmcp.example",
            $"+31 6 {rnd.Next(10_000_000, 99_999_999)}",
            HourlyRate: 60m + rnd.Next(0, 16) * 5m)).ToList();

        var customers = Enumerable.Range(0, 30).Select(i => new CustomerSeed(
            $"Customer {i + 1:D2}",
            RegionNames[i % RegionNames.Length],
            Cities[rnd.Next(Cities.Length)])).ToList();

        var products = new List<ProductSeed>();
        for (int i = 0; i < 20; i++) {
            var category = (ProductCategory)(i % 4);
            products.Add(new ProductSeed($"{category} {i / 4 + 1:D2}", category,
                UnitPrice: category switch {
                    ProductCategory.Hardware => 250m + rnd.Next(0, 30) * 25m,
                    ProductCategory.Software => 100m + rnd.Next(0, 40) * 10m,
                    ProductCategory.Services => 500m + rnd.Next(0, 20) * 50m,
                    _ => 10m + rnd.Next(0, 20) * 2m,
                }));
        }
        var byCategory = Enumerable.Range(0, products.Count)
            .GroupBy(i => products[i].Category)
            .ToDictionary(g => g.Key, g => g.ToArray());

        int PickProduct(string region, DateTime date) {
            // Bias 1: Software over-indexes in North. Bias 2: Services grows quarter-over-quarter in South.
            double softwareWeight = region == "North" ? 0.55 : 0.25;
            double monthsBack = (anchorDate - date).TotalDays / 30.4;
            double servicesWeight = region == "South" ? 0.15 + 0.35 * (1 - Math.Min(monthsBack, 24) / 24) : 0.20;
            double roll = rnd.NextDouble();
            ProductCategory cat =
                roll < softwareWeight ? ProductCategory.Software :
                roll < softwareWeight + servicesWeight ? ProductCategory.Services :
                roll < softwareWeight + servicesWeight + 0.15 ? ProductCategory.Consumables :
                ProductCategory.Hardware;
            var pool = byCategory[cat];
            return pool[rnd.Next(pool.Length)];
        }

        var orders = new List<OrderSeed>();
        for (int i = 0; i < 500; i++) {
            int customerIndex = rnd.Next(customers.Count);
            var orderDate = anchorDate.AddDays(-rnd.Next(0, 730)).Date;
            var lines = Enumerable.Range(0, rnd.Next(1, 6)).Select(_ => new LineSeed(
                PickProduct(customers[customerIndex].Region, orderDate),
                Quantity: rnd.Next(1, 20),
                Discount: rnd.Next(0, 4) * 0.05m)).ToList();
            var status = orderDate < anchorDate.AddMonths(-1)
                ? (rnd.NextDouble() < 0.08 ? OrderStatus.Cancelled : OrderStatus.Completed)
                : (OrderStatus)rnd.Next(0, 2); // New or Shipped for recent orders
            orders.Add(new OrderSeed(customerIndex, orderDate, status, lines));
        }

        var projects = Enumerable.Range(0, 6).Select(i => new ProjectSeed(
            $"Project {(char)('A' + i)}",
            CustomerIndex: rnd.Next(customers.Count),
            ManagerIndex: rnd.Next(persons.Count),
            StartDate: anchorDate.AddMonths(-rnd.Next(1, 18)).Date,
            DueDate: rnd.NextDouble() < 0.8 ? anchorDate.AddMonths(rnd.Next(-2, 7)).Date : null,
            Status: (ProjectStatus)rnd.Next(0, 4),
            Budget: 25_000m + rnd.Next(0, 30) * 5_000m)).ToList();

        string[] verbs = ["Design", "Implement", "Review", "Test", "Deploy", "Document"];
        string[] objects_ = ["data model", "import", "dashboard", "API", "security", "reports", "migration", "UI", "integration", "backlog"];
        var tasks = new List<TaskSeed>();
        for (int p = 0; p < projects.Count; p++) {
            int taskCount = 10; // 6 * 10 = 60
            for (int t = 0; t < taskCount; t++) {
                var est = 4m + rnd.Next(0, 10) * 2m;
                var status = (ProjectTaskStatus)rnd.Next(0, 4);
                tasks.Add(new TaskSeed(
                    $"{verbs[rnd.Next(verbs.Length)]} {objects_[rnd.Next(objects_.Length)]}",
                    p, rnd.Next(persons.Count), status,
                    DueDate: rnd.NextDouble() < 0.7 ? anchorDate.AddDays(rnd.Next(-60, 90)).Date : null,
                    EstimatedHours: est,
                    ActualHours: status == ProjectTaskStatus.Done ? est + rnd.Next(-4, 9) : (status == ProjectTaskStatus.InProgress ? rnd.Next(1, (int)est) : 0m)));
            }
        }

        return new DemoSet(regions, customers, persons, products, orders, projects, tasks);
    }
}

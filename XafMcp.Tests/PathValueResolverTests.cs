using XafMcp.Module.Services;

namespace XafMcp.Tests;

[TestClass]
public sealed class PathValueResolverTests {
    sealed class City { public string Name { get; set; } = ""; }
    sealed class Home { public City? City { get; set; } }
    sealed class Owner { public Home? Home { get; set; } public int Age { get; set; } }

    [TestMethod]
    public void Walks_nested_path() {
        var o = new Owner { Home = new Home { City = new City { Name = "Zwolle" } } };
        Assert.IsTrue(Equals("Zwolle", PathValueResolver.GetValue(o, "Home.City.Name")));
    }

    [TestMethod]
    public void Is_case_insensitive() {
        var o = new Owner { Age = 7 };
        Assert.IsTrue(Equals(7, PathValueResolver.GetValue(o, "age")));
    }

    [TestMethod]
    public void Null_mid_path_returns_null() {
        var o = new Owner { Home = null };
        Assert.IsNull(PathValueResolver.GetValue(o, "Home.City.Name"));
    }

    [TestMethod]
    public void Unknown_segment_throws_with_path_context() {
        // Home must be non-null: null-propagation (asserted above) short-circuits before segment validation
        var o = new Owner { Home = new Home() };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => PathValueResolver.GetValue(o, "Home.Street"));
        Assert.IsTrue(ex.Message.Contains("Street"));
    }
}

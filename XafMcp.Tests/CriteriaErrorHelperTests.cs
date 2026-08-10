using XafMcp.Module.Services;

namespace XafMcp.Tests;

[TestClass]
public sealed class CriteriaErrorHelperTests {
    // The exact message DevExpress's EF Core criteria converter produces for enum <> 'string'
    // (CLEF log 2026-08-10 05:26:24, card #1248).
    const string EnumNotEqualMessage =
        "The binary operator NotEqual is not defined for the types " +
        "'XafMcp.Module.BusinessObjects.ProjectTaskStatus' and 'System.String'.";

    [TestMethod]
    public void Enum_notequal_gets_workaround_hint() {
        var result = CriteriaErrorHelper.Describe(new InvalidOperationException(EnumNotEqualMessage));
        StringAssert.Contains(result, EnumNotEqualMessage);
        StringAssert.Contains(result, "Not (Status = 'Done')");
    }

    [TestMethod]
    public void Other_errors_pass_through_unchanged() {
        var result = CriteriaErrorHelper.Describe(new InvalidOperationException("something else broke"));
        Assert.AreEqual("something else broke", result);
    }
}

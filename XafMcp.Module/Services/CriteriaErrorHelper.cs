namespace XafMcp.Module.Services;

public static class CriteriaErrorHelper {
    // ponytail: DevExpress's EF Core criteria converter coerces enum/string for '=' but not for '<>'
    // (observed 26.1.4: Expression.NotEqual throws InvalidOperationException at materialization).
    // Map that one known trap to an actionable hint; everything else passes through untouched.
    public static string Describe(Exception ex) =>
        ex.Message.Contains("binary operator NotEqual is not defined")
            ? ex.Message + " Hint: '<>' does not work on enum properties; use Not (Property = 'Value') instead, e.g. Not (Status = 'Done')."
            : ex.Message;
}

using System.Reflection;

namespace XafMcp.Module.Services;

public static class PathValueResolver {
    /// <summary>Walks "A.B.C" via reflection. Works on EF proxies (GetProperty resolves on the proxy subclass). Null anywhere on the path yields null.</summary>
    public static object? GetValue(object root, string path) {
        object? current = root;
        foreach (var segment in path.Split('.')) {
            if (current is null) return null;
            var property = current.GetType().GetProperty(segment,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                ?? throw new ArgumentException($"Unknown property '{segment}' in path '{path}' on {current.GetType().Name}");
            current = property.GetValue(current);
        }
        return current;
    }
}

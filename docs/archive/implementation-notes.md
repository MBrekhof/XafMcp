# XafMCP Implementation Notes

## Headless XAF Bootstrap (no Blazor UI)

XAF can run without Blazor. The key is `XafApplication` with only the required modules:

```csharp
public class McpXafApplication : XafApplication
{
    protected override void CreateDefaultObjectSpaceProvider(CreateCustomObjectSpaceProviderEventArgs args)
    {
        var builder = new EFCoreObjectSpaceProviderBuilder<XafMauiEFCoreDbContext>(args.ConnectionString)
            .WithSecuredOptions(o => o.PreFetchReferenceProperties());
        args.ObjectSpaceProviders.Add(builder.Build());
        args.ObjectSpaceProviders.Add(new NonPersistentObjectSpaceProvider());
    }

    protected override LayoutManager CreateLayoutManagerCore(bool simple) => null; // Headless
}
```

Boot sequence in `Program.cs`:

```csharp
var app = new McpXafApplication();
app.Modules.Add(new XafMauiModule());      // Your business objects
app.Modules.Add(new SecurityModule());      // Permission system
app.ConnectionString = connectionString;
app.Setup();
// XafTypesInfo.Instance is now populated
// app.CreateObjectSpace() works
```

## MCP SDK Integration

Using the official `ModelContextProtocol` NuGet package:

```csharp
var builder = Host.CreateDefaultBuilder(args);
builder.ConfigureMcpServer(mcp =>
{
    mcp.AddTool<DescribeEntityTool>();
    mcp.AddTool<ListEntitiesTool>();
    mcp.AddTool<QueryTool>();
    mcp.AddTool<ExplainPermissionsTool>();
    // ... etc
});
await builder.Build().RunAsync();
```

Each tool is a class implementing `IMcpTool` with `ExecuteAsync(...)`.

## Role Impersonation for Secured Queries

To query as a specific role, create a temporary security context:

```csharp
private IObjectSpace CreateSecuredObjectSpace(Type entityType, string roleName)
{
    // Find a user with the specified role (or create a virtual one)
    var security = (SecurityStrategy)application.Security;
    var user = FindOrCreateServiceUser(roleName);
    security.Logon(user);
    return application.CreateObjectSpace(entityType);
}
```

For production, pre-create service users per role during database seeding (similar to the Hangfire pattern from xaf-security skill).

## Serialization Considerations

XAF entities are proxy objects with circular references. Serialize carefully:

```csharp
// DON'T: JsonSerializer.Serialize(xafObject) — circular refs, proxy noise
// DO: Project to DTOs
var dto = new {
    obj.GetType().GetProperty("ID").GetValue(obj),
    obj.GetType().GetProperty("Name").GetValue(obj),
    // ... selected properties only
};
```

Better approach — use `IMemberInfo` from XAF's type system:

```csharp
var typeInfo = XafTypesInfo.Instance.FindTypeInfo(entityType);
var dict = new Dictionary<string, object>();
foreach (var member in typeInfo.Members.Where(m => m.IsVisible && m.IsPersistent))
{
    dict[member.Name] = member.GetValue(obj);
}
```

## Permission Tree Traversal

```csharp
var role = os.FindObject<PermissionPolicyRole>(
    CriteriaOperator.Parse("Name = ?", roleName));

foreach (var tp in role.TypePermissions)
{
    var targetType = tp.TargetType; // Could be null if type was removed
    if (targetType == null) continue;

    // Type-level: tp.AllowRead, tp.AllowWrite, tp.AllowCreate, tp.AllowDelete, tp.AllowNavigate

    // Object-level (criteria-based):
    foreach (var op in tp.ObjectPermissions)
    {
        // op.Criteria — string like "User.ID = CurrentUserId()"
        // op.AllowRead, op.AllowWrite, etc.
    }

    // Member-level:
    foreach (var mp in tp.MemberPermissions)
    {
        // mp.Members — comma-separated property names
        // mp.AllowRead, mp.AllowWrite
    }
}
```

## Claude Code Registration

Once built, register with Claude Code:

```bash
# stdio transport (recommended)
claude mcp add xafmcp -- dotnet run --project C:\Projects\XafMCP\XafMCP.Server

# Or with environment variable for connection string
claude mcp add xafmcp -e CONNECTION_STRING="Server=localhost,1434;..." -- dotnet run --project C:\Projects\XafMCP\XafMCP.Server
```

## Performance Notes

- `XafTypesInfo.Instance` is a singleton — metadata queries are fast (in-memory)
- `CreateObjectSpace()` is lightweight but involves security evaluation
- For `xaf_query`: cap results at 100, use `TopReturnedObjects` on collection source
- For `xaf_explain_permissions`: cache role permission trees (they don't change at runtime unless PermissionsReloadMode is NoCache)
- Report generation can be slow — consider async with timeout

## Testing Strategy

1. Unit test each tool's output serialization with mock data
2. Integration test against a seeded test database
3. Manual test via Claude Code: `claude mcp add` → ask questions about entities/permissions
4. Verify security isolation: query as Consultant role should not return Admin-only data

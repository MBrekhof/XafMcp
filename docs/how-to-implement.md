# How to implement: an embedded MCP server inside a XAF Blazor Server app

This guide distills the XafMcp POC into the steps needed to give any DevExpress XAF Blazor
Server application an embedded MCP endpoint, so an LLM client (Claude Code, etc.) can query
the running app through XAF's own security. Every step points at the working file in this
repo; the gotchas at the end were all hit for real during implementation.

Architecture overview: `architecture.svg` (source `architecture.excalidraw`). Design
rationale: `superpowers/specs/2026-08-09-xafmcp-design.md`.

Versions used here: .NET 10, DevExpress 26.1.4 (EF Core), `ModelContextProtocol.AspNetCore` 2.1.0,
Serilog.AspNetCore 10.0.0.

## 1. Embed the Streamable-HTTP endpoint

Reference: `XafMcp.Blazor.Server/Startup.cs`

```csharp
// ConfigureServices — after AddXaf(...):
services.AddScoped<Mcp.McpSecurityContext>();
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<Mcp.ServerInfoTools>()
    .WithTools<Mcp.DataTools>()
    .WithTools<Mcp.SecurityTools>()
    .WithTools<Mcp.SchemaTools>()
    .WithTools<Mcp.LogTools>();

// Configure — inside UseEndpoints, next to MapXafEndpoints():
endpoints.MapMcp("/mcp");
```

Two hard rules:

- **HTTP only, no `UseHttpsRedirection()`.** MCP clients POST to `http://localhost:<port>/mcp`
  and will not follow the 307 redirect. Serve plain HTTP on a fixed port.
- The MCP endpoint lives in the **same process** as the XAF app — same DI container, same
  object space providers, same security. That is the whole point: no second deployment,
  no duplicated model.

Client registration: `claude mcp add --transport http xafmcp http://localhost:5210/mcp`.

## 2. Service user + read-only role (the security boundary)

Reference: `XafMcp.Module/DatabaseUpdate/Updater.cs`

Create a dedicated service user (`McpAgent`) and role (`McpReadOnly`) in the module updater:

- Grant **Read only** on each domain type you want exposed. XAF denies unlisted types by
  default — that default deny is your allowlist.
- Demonstrate/enforce column-level security with a member deny, e.g.
  `AddMemberPermission<Person>(Read, "HourlyRate", null, Deny)`.
- **Also grant Read on the security-metadata types** (`PermissionPolicyRole`,
  `PermissionPolicyTypePermissionObject`, `PermissionPolicyMemberPermissionsObject`,
  `PermissionPolicyUser`) — see gotcha #2. Deny `PermissionPolicyUser.StoredPassword`.

The dev password lives in `appsettings.json` (`McpAgent:Password`) and mirrored as a constant
in the updater — fine for a POC; move to real secret storage for anything shared.

## 3. Authenticate per request, hand out secured object spaces

Reference: `XafMcp.Blazor.Server/Mcp/McpSecurityContext.cs`

MCP requests arrive outside any XAF login session, so each request scope must log the service
user onto the scope's `SecurityStrategy` before touching data:

1. Register the context **scoped**; `ModelContextProtocol.AspNetCore` runs each request in
   its own DI scope, so authentication happens once per request and never leaks across.
2. On first use: resolve `ISecurityStrategyBase`; if not authenticated, build
   `AuthenticationStandardLogonParameters(UserName, password)`, call
   `Authentication.SetLogonParameters(...)`, then `Logon()` with a **non-secured** object
   space (from `INonSecuredObjectSpaceFactory`) — the logon itself must read the user row.
3. After logon, every tool gets data only via
   `serviceProvider.GetRequiredService<IObjectSpaceFactory>().CreateObjectSpace(type)` —
   the **secured** factory. No tool ever touches a non-secured space for data.

This is the load-bearing invariant: type permissions, object filters, and member denies all
come for free on every query because nothing bypasses the SecuredObjectSpace.

## 4. Tool classes

Reference: `XafMcp.Blazor.Server/Mcp/*.cs`

- A tool class is a plain DI class: `[McpServerToolType]` on the class,
  `[McpServerTool(Name = "snake_case_name")]` + `[Description]` on methods, `[Description]`
  on every parameter — the descriptions are the LLM's only manual, write them as usage hints
  (include example criteria strings, valid enum values, cross-references to other tools).
- Take dependencies (McpSecurityContext, IConfiguration, IWebHostEnvironment) via primary
  constructor.
- Return a JSON **string** (`JsonSerializer.Serialize(...)`); throw
  `ModelContextProtocol.McpException` with an actionable message for user errors (bad
  criteria, unknown entity, denied member). The message is what the LLM sees — tell it how
  to fix the call.
- **Read-only by construction:** no tool calls `CommitChanges`. Enforce it as a review rule.

## 5. Metadata-driven data tools (no per-entity code)

Reference: `EntityRegistry.cs`, `EntityProjector.cs`, `DataTools.cs`,
`XafMcp.Module/Services/PathValueResolver.cs`

- `EntityRegistry` resolves entity names via `XafTypesInfo` (`ITypeInfo`) from a fixed set of
  domain types — one list to maintain, everything else is reflection.
- `list_entities` / `describe_entity` project `ITypeInfo.Members` (names, types, enum values,
  reference targets) so the LLM can discover the model before querying.
- `query_entities` parses XAF criteria strings (`CriteriaOperator.Parse`) and runs them
  through the secured space; `EntityProjector` projects rows to dictionaries, **skipping
  denied members** (from `PermissionInspector.GetDeniedReadMembers`) so a denied column is
  absent, not null — absence can't leak.
- `aggregate_entities` walks group-by/measure paths with a small reflection resolver
  (`PathValueResolver`) and guards every path segment against member denies before querying.
- In-memory `top`/grouping is fine at POC scale; switch to `GetObjectsQuery<T>` server-side
  operators when row counts grow.

## 6. Security-insight tools

Reference: `SecurityTools.cs`, `PermissionInspector.cs`

`list_roles` / `explain_permissions` traverse the EF permission-policy object graph:
`role.TypePermissions[*].{TargetType, ReadState, WriteState, ...}`, `.ObjectPermissions[*]
.Criteria`, `.MemberPermissions[*].Members` (semicolon-separated). Read them through the
secured space — which is why step 2 grants read on the security-metadata types.

## 7. Schema-drift tool

Reference: `SchemaTools.cs`, `XafMcp.Module/Schema/SchemaDriftComparer.cs`

Two readers feed one pure comparer (unit-testable, no DB in tests):

- Model side: a standalone `DbContextOptionsBuilder<YourDbContext>().UseSqlServer(cs)
  .UseChangeTrackingProxies()` context (gotcha #3), walk
  `ctx.Model.GetRelationalModel().Tables` for name/store-type/nullability.
- DB side: plain `SqlConnection` over `INFORMATION_SCHEMA.COLUMNS`, composing store-type
  strings in EF's vocabulary (`nvarchar(100)`, `decimal(19,4)`, `-1` → `max`).

The comparer reports `table_only_in_db`, `column_only_in_db`, `column_missing_in_db`
(severity error — EF will fail reading), `type_mismatch`, `nullability_mismatch`.

## 8. Log-forensics tools

Reference: `Program.cs` (sink), `XafMcp.Module/Logs/ClefParser.cs`, `LogTools.cs`

- Serilog writes CLEF JSON: `.WriteTo.File(new CompactJsonFormatter(), "logs/xafmcp-.clef",
  rollingInterval: Day, shared: true)`. The rolling filename becomes `xafmcp-YYYYMMDD.clef` —
  the date is inserted before the extension, so the template is `xafmcp-.clef`, not
  `xafmcp.clef`.
- The parser opens files with `FileShare.ReadWrite` (Serilog holds the current file open) and
  returns null for malformed lines instead of throwing.
- `search_logs` filters by time window / min level / substring / regex (with a regex
  timeout); `summarize_logs` aggregates counts per level, top exception types, top sources.

## 9. Tests and verification

- **Unit (`XafMcp.Tests`)**: only the pure Module logic — demo-data bias, drift comparer,
  CLEF parser, path resolver. References the Module project only, never the Blazor host.
- **Smoke without an LLM**: `scripts/mcp-call.ps1 -Tool <name> -ArgsJson '{...}'` (or
  `-List`) speaks the initialize/tools-call handshake over plain HTTP.
- **E2E (`XafMcp.E2E`)**: Playwright login smoke + raw MCP HTTP smoke (all tools listed;
  a query on the member-denied type must NOT contain the denied member name). Tests
  `Assert.Ignore` when the app is down so they never silently pass.
- Negative tests matter most: denied member refused in `aggregate_entities`, absent in
  `query_entities`, named as deny in `explain_permissions`.

## Gotchas (all hit during implementation)

1. **HTTPS redirect breaks MCP clients** — no `UseHttpsRedirection()`; plain HTTP.
2. **Security-metadata reads** — introspecting permissions through the secured space
   silently returns an empty role list unless the role can read the PermissionPolicy*
   types. Grant read on them (and deny `StoredPassword`).
3. **Standalone DbContext fails model validation** — XAF contexts use
   `ChangingAndChangedNotificationsWithOriginalValues`; building the context outside XAF
   throws "does not implement INotifyPropertyChanged" unless you add
   `.UseChangeTrackingProxies()` to the options.
4. **`Debugger.IsAttached` gates DB auto-update** — under `dotnet run` the app never
   updates the database; run `-- --updateDatabase --forceUpdate --silent` explicitly.
5. **Serilog rolling file naming** — template `logs/xafmcp-.clef` yields
   `xafmcp-YYYYMMDD.clef`; get this wrong and the log tools' glob finds nothing.
6. **Stock `dotnet new gitignore` swallows source dirs** — `[Ll]ogs/` matched
   `Module/Logs/` (the CLEF parser) and `*.e2e` matched the whole `XafMcp.E2E/` project
   directory, both silently. Scope or delete those patterns.
7. **XAF Blazor E2E selectors** — nav items are captioned singular ("Customer"); prefer
   role-based Playwright locators (`GetByRole`) — CSS alternations can click the login
   page's hidden Enter-key submit button.
8. **A running app locks build output** — stop the app (`scripts/stop-app.ps1`) before
   `dotnet build`.
9. **Enum `<>` criteria crash past the parse guard** — `Status <> 'Done'` parses fine, but
   the EF Core criteria converter only coerces enum↔string for `=`, not `<>`
   (`Expression.NotEqual` throws `InvalidOperationException` at materialization, observed
   26.1.4). Guard materialization too, not just `CriteriaOperator.Parse`, and hint the
   client toward `Not (Status = 'Done')` — an LLM client self-corrects off a good message.

## Porting checklist

- [ ] Add `ModelContextProtocol.AspNetCore` + Serilog packages to the Blazor.Server project
- [ ] `AddMcpServer().WithHttpTransport().WithTools<...>()` + `MapMcp("/mcp")`; remove
      `UseHttpsRedirection`
- [ ] Updater: service user + read-only role (domain types + security-metadata types,
      member denies for sensitive columns)
- [ ] `McpSecurityContext` (scoped logon → secured object spaces)
- [ ] Tool classes for your domain (start from this repo's `Mcp/` folder; `EntityRegistry`
      is the only place that names your entity types)
- [ ] `mcp-call.ps1` smoke + negative tests for every member deny

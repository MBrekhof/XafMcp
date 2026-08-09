# XafMcp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An XAF Blazor Server LOB app (8 entities, LocalDB) exposing an embedded Streamable-HTTP MCP endpoint with 10 read-only tools: metadata/query/aggregate, security insight, schema drift, log forensics, server info.

**Architecture:** Standard XAF pair — `XafMcp.Module` (entities, DbContext, updater/seed, pure services) + `XafMcp.Blazor.Server` (XAF UI + Serilog CLEF + `ModelContextProtocol.AspNetCore` endpoint + tool classes). All MCP data access runs through a SecuredObjectSpace as the `McpAgent` service user. Pure logic (demo generator, drift comparer, CLEF parser, path resolver) lives in the Module so `XafMcp.Tests` never references the Blazor host.

**Tech Stack:** .NET 10 · DevExpress XAF **26.1.4** (EF Core) · SQL Server LocalDB · `ModelContextProtocol.AspNetCore` (prerelease) · Serilog (`Serilog.AspNetCore` + `Serilog.Formatting.Compact`) · MSTest 4 · NUnit + Microsoft.Playwright.NUnit (E2E only).

**Authoritative spec:** `docs/superpowers/specs/2026-08-09-xafmcp-design.md`. Useful reference snippets (IMemberInfo projection, permission traversal): `docs/archive/implementation-notes.md`.

## Global Constraints

- Repo root: `C:\Projects\XafMCP` (on-disk casing is `XafMCP`; all namespaces/projects/solution use **XafMcp**).
- Every DevExpress package pinned to **26.1.4** — never `26.1.*`, never mixed patch levels.
- TargetFramework `net10.0` everywhere. EF Core packages at `10.0.3` (if NuGet reports a version conflict with DevExpress 26.1.4, align to the version the DX packages request — read the NU1107 message, don't guess).
- App URL: `http://localhost:5210` only (no HTTPS, no `UseHttpsRedirection` — MCP clients won't follow the 307).
- Database: `Data Source=(localdb)\mssqllocaldb;Integrated Security=SSPI;MultipleActiveResultSets=True;Initial Catalog=XafMcp` (no `EFCoreProvider=` prefix for SQL Server — verified against a working template).
- Entity rules (from the `xaf-efcore-entities` skill — violations fail **silently**): every property `virtual`; collections `ObservableCollection<T>` typed as `IList<T>`, child collections `[Aggregated]`; explicit FK properties with `[ForeignKey]`; decimal precision set in `OnModelCreating`; no `OwnsOne`.
- The task entity is `ProjectTask` — never `Task` (collides with `System.Threading.Tasks.Task`).
- All 10 MCP tools are read-only: no tool ever calls `CommitChanges`.
- MSTest 4 assertion forms: `Assert.IsEmpty` / `Assert.HasCount` / `Assert.IsTrue` / `Assert.IsNull` — **not** `Assert.AreEqual(0, x.Count)` (analyzer MSTEST0037 floods the build otherwise).
- Windows/PowerShell environment: call `python` never `python3` (n/a here), use `taskkill //PID <pid> //F //T` from Git Bash or `taskkill /PID <pid> /F /T` from PowerShell.
- **Git: stage exact files only.** Never `git add -A`, `git add .`, or `git commit -am`. Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` (use two `-m` flags, not a here-string).
- The app must be **stopped** (`scripts/stop-app.ps1`) before `dotnet build` — a running app locks the output DLLs.
- Template ground truth for anything not spelled out here: `C:\Projects\XafRag\XafRag\` (a working 26.1/.NET 10 XAF Blazor app). Copy shapes from there; do not invent.

## File Map (final state)

```
XafMcp.sln
scripts/run-app.ps1, stop-app.ps1, mcp-call.ps1
docs/drift-demo.sql
XafMcp.Module/
  XafMcp.Module.csproj, Module.cs, Model.DesignedDiffs.xafml
  BusinessObjects/BaseObjectInt.cs, Enums.cs, Region.cs, Customer.cs, Person.cs,
    Product.cs, Order.cs, OrderLine.cs, Project.cs, ProjectTask.cs, XafMcpEFCoreDbContext.cs
  DatabaseUpdate/Updater.cs
  DemoData/DemoDataGenerator.cs
  Schema/SchemaDriftComparer.cs        (pure: records + comparer)
  Logs/ClefParser.cs                   (pure: parser + record)
  Services/PathValueResolver.cs        (pure: reflection path walker)
XafMcp.Blazor.Server/
  XafMcp.Blazor.Server.csproj, Program.cs, Startup.cs, BlazorApplication.cs, BlazorModule.cs,
  Model.xafml, appsettings.json, Properties/launchSettings.json,
  App.razor, _Imports.razor, Pages/_Host.cshtml,
  Services/CircuitHandlerProxy.cs, Services/ProxyHubConnectionHandler.cs, wwwroot/*   (copied from XafRag)
  Mcp/JsonOpts.cs, EntityRegistry.cs, EntityProjector.cs, PermissionInspector.cs, McpSecurityContext.cs,
    ServerInfoTools.cs, DataTools.cs, SecurityTools.cs, SchemaTools.cs, LogTools.cs
XafMcp.Tests/        (MSTest 4; references Module only)
XafMcp.E2E/          (NUnit + Playwright; login smoke + MCP HTTP smoke)
```

---

### Task 1: Solution scaffold — buildable, bootable XAF Blazor app

**Files:**
- Create: `XafMcp.sln`, `.gitignore`, `scripts/run-app.ps1`, `scripts/stop-app.ps1`
- Create: `XafMcp.Module/XafMcp.Module.csproj`, `XafMcp.Module/Module.cs`, `XafMcp.Module/DatabaseUpdate/Updater.cs` (empty shell), `XafMcp.Module/BusinessObjects/XafMcpEFCoreDbContext.cs` (security types only)
- Create: `XafMcp.Blazor.Server/XafMcp.Blazor.Server.csproj`, `Program.cs`, `Startup.cs`, `BlazorApplication.cs`, `appsettings.json`, `Properties/launchSettings.json`
- Copy from `C:\Projects\XafRag\XafRag\` (namespace-swapped): `App.razor`, `_Imports.razor`, `Pages/_Host.cshtml`, `Services/CircuitHandlerProxy.cs`, `Services/ProxyHubConnectionHandler.cs`, `BlazorModule.cs`, `Model.xafml`, `wwwroot/*`, and the Module's `Model.DesignedDiffs.xafml`

**Interfaces:**
- Consumes: nothing (first task)
- Produces: bootable app on `:5210`; `XafMcpEFCoreDbContext` (namespace `XafMcp.Module.BusinessObjects`); `XafMcpModule` (namespace `XafMcp.Module`); `Updater` shell (namespace `XafMcp.Module.DatabaseUpdate`); `scripts/run-app.ps1` / `stop-app.ps1` used by every later task's verification

- [ ] **Step 1: gitignore + solution shell**

```powershell
cd C:\Projects\XafMCP
dotnet new gitignore
Add-Content .gitignore "`n.app.pid`nlogs/"
dotnet new sln -n XafMcp
```

- [ ] **Step 2: Module project**

`XafMcp.Module/XafMcp.Module.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Deterministic>false</Deterministic>
    <AssemblyVersion>1.0.*</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <Configurations>Debug;Release;EasyTest</Configurations>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <None Remove="Model.DesignedDiffs.xafml" />
    <EmbeddedResource Include="Model.DesignedDiffs.xafml" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="DevExpress.ExpressApp" Version="26.1.4" />
    <PackageReference Include="DevExpress.ExpressApp.CodeAnalysis" Version="26.1.4" />
    <PackageReference Include="DevExpress.ExpressApp.EFCore" Version="26.1.4" />
    <PackageReference Include="DevExpress.ExpressApp.Validation" Version="26.1.4" />
    <PackageReference Include="DevExpress.Persistent.Base" Version="26.1.4" />
    <PackageReference Include="DevExpress.Persistent.BaseImpl.EFCore" Version="26.1.4" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.2" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Proxies" Version="10.0.3" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="10.0.3" />
  </ItemGroup>
</Project>
```

Copy `C:\Projects\XafRag\XafRag\XafRag.Module\Model.DesignedDiffs.xafml` to `XafMcp.Module\Model.DesignedDiffs.xafml` unchanged (it is a near-empty XML shell).

`XafMcp.Module/Module.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Updating;

namespace XafMcp.Module;

public sealed class XafMcpModule : ModuleBase {
    public XafMcpModule() {
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.SystemModule.SystemModule));
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Security.SecurityModule));
        RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Validation.ValidationModule));
        DevExpress.ExpressApp.Security.SecurityModule.UsedExportedTypes = DevExpress.Persistent.Base.UsedExportedTypes.Custom;
        AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.PermissionPolicy.PermissionPolicyUser));
        AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.PermissionPolicy.PermissionPolicyRole));
        AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.ModelDifference));
        AdditionalExportedTypes.Add(typeof(DevExpress.Persistent.BaseImpl.EF.ModelDifferenceAspect));
    }
    public override IEnumerable<ModuleUpdater> GetModuleUpdaters(IObjectSpace objectSpace, Version versionFromDB) {
        return new ModuleUpdater[] { new DatabaseUpdate.Updater(objectSpace, versionFromDB) };
    }
}
```

`XafMcp.Module/DatabaseUpdate/Updater.cs` (shell — Task 4 fills it):

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Updating;

namespace XafMcp.Module.DatabaseUpdate;

public class Updater : ModuleUpdater {
    public Updater(IObjectSpace objectSpace, Version currentDBVersion) : base(objectSpace, currentDBVersion) { }
}
```

`XafMcp.Module/BusinessObjects/XafMcpEFCoreDbContext.cs` (security types only in this task):

```csharp
using DevExpress.ExpressApp.Design;
using DevExpress.ExpressApp.EFCore.DesignTime;
using DevExpress.Persistent.BaseImpl.EF;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.EntityFrameworkCore;

namespace XafMcp.Module.BusinessObjects;

[TypesInfoInitializer(typeof(DbContextTypesInfoInitializer<XafMcpEFCoreDbContext>))]
public class XafMcpEFCoreDbContext : DbContext {
    public XafMcpEFCoreDbContext(DbContextOptions<XafMcpEFCoreDbContext> options) : base(options) { }
    public DbSet<ModelDifference> ModelDifferences { get; set; }
    public DbSet<ModelDifferenceAspect> ModelDifferenceAspects { get; set; }
    public DbSet<PermissionPolicyRole> Roles { get; set; }
    public DbSet<PermissionPolicyUser> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        base.OnModelCreating(modelBuilder);
        modelBuilder.UseDeferredDeletion(this);
        modelBuilder.UseOptimisticLock();
        modelBuilder.SetOneToManyAssociationDeleteBehavior(DeleteBehavior.SetNull, DeleteBehavior.Cascade);
        modelBuilder.HasChangeTrackingStrategy(ChangeTrackingStrategy.ChangingAndChangedNotificationsWithOriginalValues);
        modelBuilder.UsePropertyAccessMode(PropertyAccessMode.PreferFieldDuringConstruction);
        modelBuilder.Entity<ModelDifference>()
            .HasMany(t => t.Aspects)
            .WithOne(t => t.Owner)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Blazor.Server project**

`XafMcp.Blazor.Server/XafMcp.Blazor.Server.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Deterministic>false</Deterministic>
    <AssemblyVersion>1.0.*</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <Configurations>Debug;Release;EasyTest</Configurations>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <None Remove="Model.xafml" />
    <Content Include="Model.xafml">
      <CopyToOutputDirectory>Always</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="DevExpress.Drawing.Skia" Version="26.1.4" />
    <PackageReference Include="DevExpress.ExpressApp.Blazor" Version="26.1.4" />
    <PackageReference Include="DevExpress.ExpressApp.CodeAnalysis" Version="26.1.4" />
    <PackageReference Include="DevExpress.ExpressApp.Validation.Blazor" Version="26.1.4" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\XafMcp.Module\XafMcp.Module.csproj" />
  </ItemGroup>
</Project>
```

`Program.cs` — copy `C:\Projects\XafRag\XafRag\XafRag.Blazor.Server\Program.cs` and apply exactly these edits: namespace → `XafMcp.Blazor.Server`; class `XafRagBlazorApplication` → `XafMcpBlazorApplication` (via `UseStartup<Startup>` nothing else changes); **delete** the `.UseSerilog(...)` block and the `using Serilog;` line (Serilog arrives in Task 5). Keep the `--updateDatabase` / `IDBUpdater` handling and the `IDesignTimeApplicationFactory` implementation — they are load-bearing (headless DB creation, see Step 6).

`BlazorApplication.cs` — copy XafRag's `BlazorApplication.cs`, rename class to `XafMcpBlazorApplication`, `ApplicationName = "XafMcp"`, namespace `XafMcp.Blazor.Server`. Keep the `Debugger.IsAttached` blocks exactly as-is (known trap: without a debugger the app never auto-updates the DB — that is why Step 6 runs `--updateDatabase` explicitly).

`Startup.cs` — XafRag's Startup reduced. Full content:

```csharp
using DevExpress.ExpressApp.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.ApplicationBuilder;
using DevExpress.ExpressApp.Blazor.Services;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.EntityFrameworkCore;
using XafMcp.Blazor.Server.Services;
using XafMcp.Module.BusinessObjects;

namespace XafMcp.Blazor.Server;

public class Startup {
    public Startup(IConfiguration configuration) {
        Configuration = configuration;
    }
    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton(typeof(Microsoft.AspNetCore.SignalR.HubConnectionHandler<>), typeof(ProxyHubConnectionHandler<>));
        services.AddRazorPages();
        services.AddServerSideBlazor();
        services.AddHttpContextAccessor();
        services.AddScoped<CircuitHandler, CircuitHandlerProxy>();
        services.AddXaf(Configuration, builder => {
            builder.UseApplication<XafMcpBlazorApplication>();
            builder.Modules
                .AddValidation(options => {
                    options.AllowValidationDetailsAccess = false;
                })
                .Add<XafMcp.Module.XafMcpModule>()
                .Add<XafMcpBlazorModule>();
            builder.ObjectSpaceProviders
                .AddSecuredEFCore(options => {
                    options.PreFetchReferenceProperties();
                })
                .WithDbContext<XafMcpEFCoreDbContext>((serviceProvider, options) => {
                    string? connectionString = Configuration.GetConnectionString("ConnectionString");
                    ArgumentNullException.ThrowIfNull(connectionString);
                    options.UseConnectionString(connectionString);
                })
                .AddNonPersistent();
            builder.Security
                .UseIntegratedMode(options => {
                    options.Lockout.Enabled = true;
                    options.RoleType = typeof(PermissionPolicyRole);
                    options.UserType = typeof(PermissionPolicyUser);
                    options.Events.OnSecurityStrategyCreated += securityStrategy => {
                        ((SecurityStrategy)securityStrategy).PermissionsReloadMode = PermissionsReloadMode.NoCache;
                    };
                })
                .AddPasswordAuthentication(options => {
                    options.IsSupportChangePassword = true;
                });
        });
        var authentication = services.AddAuthentication(options => {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        });
        authentication.AddCookie(options => {
            options.LoginPath = "/LoginPage";
        });
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
        if (env.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        }
        else {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
        }
        // ponytail: no UseHttpsRedirection — MCP clients POST to http://localhost:5210/mcp and won't follow a 307
        app.UseRequestLocalization();
        app.UseStaticFiles();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();
        app.UseXaf();
        app.UseEndpoints(endpoints => {
            endpoints.MapXafEndpoints();
            endpoints.MapBlazorHub();
            endpoints.MapFallbackToPage("/_Host");
        });
    }
}
```

`appsettings.json`:

```json
{
  "ConnectionStrings": {
    "ConnectionString": "Data Source=(localdb)\\mssqllocaldb;Integrated Security=SSPI;MultipleActiveResultSets=True;Initial Catalog=XafMcp",
    "EasyTestConnectionString": "Data Source=(localdb)\\mssqllocaldb;Integrated Security=SSPI;MultipleActiveResultSets=True;Initial Catalog=XafMcpEasyTest"
  },
  "McpAgent": {
    "Password": "mcp-agent-dev"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "DevExpress.ExpressApp": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

`Properties/launchSettings.json`:

```json
{
  "profiles": {
    "XafMcp.Blazor.Server": {
      "commandName": "Project",
      "launchBrowser": false,
      "applicationUrl": "http://localhost:5210",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

- [ ] **Step 4: copy the framework content files**

```powershell
$src = 'C:\Projects\XafRag\XafRag\XafRag.Blazor.Server'
$dst = 'C:\Projects\XafMCP\XafMcp.Blazor.Server'
New-Item -ItemType Directory -Force "$dst\Pages","$dst\Services" | Out-Null
Copy-Item "$src\App.razor","$src\_Imports.razor","$src\Model.xafml","$src\BlazorModule.cs" $dst
Copy-Item "$src\Pages\_Host.cshtml" "$dst\Pages\"
Copy-Item "$src\Services\CircuitHandlerProxy.cs","$src\Services\ProxyHubConnectionHandler.cs" "$dst\Services\"
Copy-Item "$src\wwwroot" $dst -Recurse
# namespace + name swap in every copied text file
Get-ChildItem $dst -Recurse -File -Include *.razor,*.cs,*.cshtml,*.xafml | ForEach-Object {
  (Get-Content $_.FullName -Raw) -replace 'XafRag', 'XafMcp' | Set-Content $_.FullName -Encoding utf8
}
```

Then open the copied `BlazorModule.cs` and `_Host.cshtml` and confirm nothing OpenAI/RAG-specific survived the copy (XafRag's `BlazorModule.cs` is the standard template module; if it references `RagChatDetailViewUpdater` or similar, delete those lines — the standard shell is just `RequiredModuleTypes.Add(typeof(DevExpress.ExpressApp.Blazor.SystemModule.SystemBlazorModule))`).

- [ ] **Step 5: add projects to solution and build**

```powershell
cd C:\Projects\XafMCP
dotnet sln add XafMcp.Module XafMcp.Blazor.Server
dotnet build XafMcp.sln
```

Expected: `Build succeeded`. Fix compile errors by comparing against the XafRag source files — do not remove XAF wiring to silence errors.

- [ ] **Step 6: create the database (headless — Debugger.IsAttached is false under dotnet run)**

```powershell
dotnet run --project XafMcp.Blazor.Server -- --updateDatabase --forceUpdate --silent
```

Expected: exit code 0 (`$LASTEXITCODE -eq 0`).

- [ ] **Step 7: run/stop scripts + boot check**

`scripts/run-app.ps1`:

```powershell
param([int]$TimeoutSec = 90)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path "$root\.app.pid") { & "$PSScriptRoot\stop-app.ps1" }
$proc = Start-Process dotnet -ArgumentList 'run','--project',"$root\XafMcp.Blazor.Server" -PassThru -WindowStyle Hidden -WorkingDirectory $root
$proc.Id | Set-Content "$root\.app.pid"
$deadline = (Get-Date).AddSeconds($TimeoutSec)
while ((Get-Date) -lt $deadline) {
    try {
        $r = Invoke-WebRequest 'http://localhost:5210/LoginPage' -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { Write-Host "App up (pid $($proc.Id))"; exit 0 }
    } catch { Start-Sleep -Milliseconds 750 }
}
& "$PSScriptRoot\stop-app.ps1"
Write-Error 'App did not come up in time'
```

`scripts/stop-app.ps1`:

```powershell
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path "$root\.app.pid") {
    $appPid = Get-Content "$root\.app.pid"
    taskkill /PID $appPid /F /T 2>$null
    Remove-Item "$root\.app.pid"
}
# safety net: anything still bound to :5210
Get-NetTCPConnection -LocalPort 5210 -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { taskkill /PID $_.OwningProcess /F /T 2>$null }
Write-Host 'App stopped'
```

Run: `./scripts/run-app.ps1` → expect `App up`; then `./scripts/stop-app.ps1`.

- [ ] **Step 8: Commit**

```powershell
git add .gitignore XafMcp.sln scripts XafMcp.Module XafMcp.Blazor.Server
git commit -m "feat: XAF Blazor Server scaffold on 26.1.4 (boots on :5210, LocalDB)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Entities, DbContext registration, model smoke tests

**Files:**
- Create: `XafMcp.Module/BusinessObjects/BaseObjectInt.cs`, `Enums.cs`, `Region.cs`, `Customer.cs`, `Person.cs`, `Product.cs`, `Order.cs`, `OrderLine.cs`, `Project.cs`, `ProjectTask.cs`
- Modify: `XafMcp.Module/BusinessObjects/XafMcpEFCoreDbContext.cs` (DbSets + precision)
- Create: `XafMcp.Tests/XafMcp.Tests.csproj`, `XafMcp.Tests/ModelSmokeTests.cs`

**Interfaces:**
- Consumes: `XafMcpEFCoreDbContext` from Task 1
- Produces: the 8 entity types in `XafMcp.Module.BusinessObjects` (exact property names below — later tasks reference `Person.HourlyRate`, `Order.Total`, `Customer.Region.Name` verbatim); enums `ProductCategory`, `OrderStatus`, `ProjectStatus`, `ProjectTaskStatus`; DbSet names = table names: `Regions`, `Customers`, `Persons`, `Products`, `Orders`, `OrderLines`, `Projects`, `ProjectTasks`

- [ ] **Step 1: Tests project + failing test**

`XafMcp.Tests/XafMcp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.1.0" />
    <PackageReference Include="MSTest" Version="4.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\XafMcp.Module\XafMcp.Module.csproj" />
  </ItemGroup>
</Project>
```

`XafMcp.Tests/ModelSmokeTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using XafMcp.Module.BusinessObjects;

namespace XafMcp.Tests;

[TestClass]
public sealed class ModelSmokeTests {
    static XafMcpEFCoreDbContext CreateContext() {
        var options = new DbContextOptionsBuilder<XafMcpEFCoreDbContext>()
            .UseSqlServer("Data Source=(localdb)\\mssqllocaldb;Integrated Security=SSPI;Initial Catalog=XafMcpModelOnly")
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test XafMcp.Tests` — Expected: FAIL (compile error: entity types missing, or missing-table assertion).

- [ ] **Step 3: Entities**

`BaseObjectInt.cs` (house pattern from the xaf-efcore-entities skill, origin XafSearch):

```csharp
using System.ComponentModel.DataAnnotations;
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

public abstract class BaseObjectInt : IXafEntityObject, IObjectSpaceLink {
    protected IObjectSpace? ObjectSpace;

    [Key]
    [VisibleInListView(false)]
    [VisibleInDetailView(false)]
    [VisibleInLookupListView(false)]
    public virtual int ID { get; set; }

    IObjectSpace IObjectSpaceLink.ObjectSpace {
        get => ObjectSpace!;
        set => ObjectSpace = value;
    }

    public virtual void OnCreated() { }
    public virtual void OnSaving() { }
    public virtual void OnLoaded() { }
}
```

`Enums.cs`:

```csharp
namespace XafMcp.Module.BusinessObjects;

public enum ProductCategory { Hardware, Software, Services, Consumables }
public enum OrderStatus { New, Shipped, Completed, Cancelled }
public enum ProjectStatus { Planned, Active, OnHold, Completed }
public enum ProjectTaskStatus { Open, InProgress, Blocked, Done }
```

`Region.cs`:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Region : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;
}
```

`Customer.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Customer : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;

    public virtual int? RegionId { get; set; }
    [ForeignKey(nameof(RegionId))]
    public virtual Region? Region { get; set; }

    [MaxLength(100)]
    public virtual string City { get; set; } = string.Empty;

    public virtual IList<Order> Orders { get; set; } = new ObservableCollection<Order>();
    public virtual IList<Project> Projects { get; set; } = new ObservableCollection<Project>();
}
```

`Person.cs`:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(FullName))]
public class Person : BaseObjectInt {
    [Required]
    [MaxLength(60)]
    public virtual string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(60)]
    public virtual string LastName { get; set; } = string.Empty;

    [MaxLength(200)]
    public virtual string Email { get; set; } = string.Empty;

    [MaxLength(40)]
    public virtual string Phone { get; set; } = string.Empty;

    // Member-denied to the MCP role — the security demo (spec §4)
    public virtual decimal HourlyRate { get; set; }

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();
}
```

`Product.cs`:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Product : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;

    public virtual ProductCategory Category { get; set; }
    public virtual decimal UnitPrice { get; set; }
    public virtual bool Active { get; set; } = true;
}
```

`Order.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(DisplayName))]
public class Order : BaseObjectInt {
    public virtual int? CustomerId { get; set; }
    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    public virtual DateTime OrderDate { get; set; }
    public virtual OrderStatus Status { get; set; }

    // Stored so XAF criteria strings can filter on it server-side ("Total > 1000")
    public virtual decimal Total { get; set; }

    [Aggregated]
    public virtual IList<OrderLine> Lines { get; set; } = new ObservableCollection<OrderLine>();

    [NotMapped]
    public string DisplayName => $"Order {ID} ({OrderDate:yyyy-MM-dd})";

    public override void OnSaving() {
        base.OnSaving();
        Total = Lines.Sum(l => l.LineTotal);
    }
}
```

`OrderLine.cs`:

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace XafMcp.Module.BusinessObjects;

public class OrderLine : BaseObjectInt {
    public virtual int? OrderId { get; set; }
    [ForeignKey(nameof(OrderId))]
    public virtual Order? Order { get; set; }

    public virtual int? ProductId { get; set; }
    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    public virtual int Quantity { get; set; }
    public virtual decimal UnitPrice { get; set; }
    public virtual decimal Discount { get; set; } // fraction 0..1

    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice * (1m - Discount);
}
```

`Project.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Project : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;

    public virtual int? CustomerId { get; set; }
    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    public virtual int? ManagerId { get; set; }
    [ForeignKey(nameof(ManagerId))]
    public virtual Person? Manager { get; set; }

    public virtual DateTime StartDate { get; set; }
    public virtual DateTime? DueDate { get; set; }
    public virtual ProjectStatus Status { get; set; }
    public virtual decimal Budget { get; set; }

    [Aggregated]
    public virtual IList<ProjectTask> Tasks { get; set; } = new ObservableCollection<ProjectTask>();
}
```

`ProjectTask.cs`:

```csharp
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Subject))]
public class ProjectTask : BaseObjectInt {
    [Required]
    [MaxLength(200)]
    public virtual string Subject { get; set; } = string.Empty;

    public virtual int? ProjectId { get; set; }
    [ForeignKey(nameof(ProjectId))]
    public virtual Project? Project { get; set; }

    public virtual int? AssignedToId { get; set; }
    [ForeignKey(nameof(AssignedToId))]
    public virtual Person? AssignedTo { get; set; }

    public virtual ProjectTaskStatus Status { get; set; }
    public virtual DateTime? DueDate { get; set; }
    public virtual decimal EstimatedHours { get; set; }
    public virtual decimal ActualHours { get; set; }
}
```

- [ ] **Step 4: DbContext — DbSets + precision**

Add to `XafMcpEFCoreDbContext`:

```csharp
public DbSet<Region> Regions { get; set; }
public DbSet<Customer> Customers { get; set; }
public DbSet<Person> Persons { get; set; }
public DbSet<Product> Products { get; set; }
public DbSet<Order> Orders { get; set; }
public DbSet<OrderLine> OrderLines { get; set; }
public DbSet<Project> Projects { get; set; }
public DbSet<ProjectTask> ProjectTasks { get; set; }
```

And in `OnModelCreating`, after the existing calls:

```csharp
modelBuilder.Entity<Person>().Property(p => p.HourlyRate).HasPrecision(19, 4);
modelBuilder.Entity<Product>().Property(p => p.UnitPrice).HasPrecision(19, 4);
modelBuilder.Entity<Order>().Property(p => p.Total).HasPrecision(19, 4);
modelBuilder.Entity<OrderLine>().Property(p => p.UnitPrice).HasPrecision(19, 4);
modelBuilder.Entity<OrderLine>().Property(p => p.Discount).HasPrecision(19, 4);
modelBuilder.Entity<Project>().Property(p => p.Budget).HasPrecision(19, 4);
modelBuilder.Entity<ProjectTask>().Property(p => p.EstimatedHours).HasPrecision(19, 4);
modelBuilder.Entity<ProjectTask>().Property(p => p.ActualHours).HasPrecision(19, 4);
```

- [ ] **Step 5: Run tests to verify they pass**

```powershell
dotnet sln add XafMcp.Tests
dotnet test XafMcp.Tests
```

Expected: PASS (2/2).

- [ ] **Step 6: Recreate the database with the new schema and confirm the app still boots**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('XafMcp') IS NOT NULL BEGIN ALTER DATABASE XafMcp SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE XafMcp; END"
dotnet run --project XafMcp.Blazor.Server -- --updateDatabase --forceUpdate --silent
./scripts/run-app.ps1
./scripts/stop-app.ps1
```

Expected: updater exit 0, `App up`.

- [ ] **Step 7: Commit**

```powershell
git add XafMcp.sln XafMcp.Module/BusinessObjects XafMcp.Tests
git commit -m "feat: 8 domain entities + DbContext registration with model smoke tests" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Deterministic demo-data generator (pure) with bias tests

**Files:**
- Create: `XafMcp.Module/DemoData/DemoDataGenerator.cs`
- Create: `XafMcp.Tests/DemoDataGeneratorTests.cs`

**Interfaces:**
- Consumes: enums from Task 2
- Produces: `DemoDataGenerator.Generate(DateTime anchorDate, int seed = 42)` returning `DemoSet`; record shapes below are consumed verbatim by Task 4's Updater

- [ ] **Step 1: Write the failing tests**

`XafMcp.Tests/DemoDataGeneratorTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test XafMcp.Tests --filter DemoDataGeneratorTests` — Expected: FAIL (type `DemoDataGenerator` not defined).

- [ ] **Step 3: Implement the generator**

`XafMcp.Module/DemoData/DemoDataGenerator.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test XafMcp.Tests --filter DemoDataGeneratorTests` — Expected: PASS (4/4). If `North_overindexes_on_software` fails, raise the North `softwareWeight` (e.g. 0.55 → 0.65) rather than loosening the assertion.

- [ ] **Step 5: Commit**

```powershell
git add XafMcp.Module/DemoData/DemoDataGenerator.cs XafMcp.Tests/DemoDataGeneratorTests.cs
git commit -m "feat: deterministic demo-data generator with regional bias, unit-tested" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Updater — security seed + demo data persisted

**Files:**
- Modify: `XafMcp.Module/DatabaseUpdate/Updater.cs` (replace the shell)

**Interfaces:**
- Consumes: `DemoDataGenerator.Generate(DateTime, int)` (Task 3); entities (Task 2)
- Produces: users `Admin` (empty dev password, admin role) and `McpAgent` (password = the `McpAgent:Password` config value, defaulted to `mcp-agent-dev` — Task 8 reads the same key); role `McpReadOnly` (read-all-domain-types, member-deny `Person.HourlyRate`, zero writes). Later tasks depend on these exact names: `McpAgent`, `McpReadOnly`.

- [ ] **Step 1: Implement the Updater**

Replace `XafMcp.Module/DatabaseUpdate/Updater.cs` with:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Security;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using XafMcp.Module.BusinessObjects;
using XafMcp.Module.DemoData;

namespace XafMcp.Module.DatabaseUpdate;

public class Updater : ModuleUpdater {
    public const string AdminUserName = "Admin";
    public const string McpUserName = "McpAgent";
    public const string McpRoleName = "McpReadOnly";
    // POC-only: dev password also present in appsettings.json (McpAgent:Password).
    // ModuleUpdater has no IConfiguration; config-ify when/if this repo goes public.
    public const string McpDevPassword = "mcp-agent-dev";

    public Updater(IObjectSpace objectSpace, Version currentDBVersion) : base(objectSpace, currentDBVersion) { }

    public override void UpdateDatabaseAfterUpdateSchema() {
        base.UpdateDatabaseAfterUpdateSchema();
        CreateSecurityObjects();
        SeedDemoData();
        ObjectSpace.CommitChanges();
    }

    void CreateSecurityObjects() {
        var adminRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == "Administrators");
        if (adminRole == null) {
            adminRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            adminRole.Name = "Administrators";
            adminRole.IsAdministrative = true;
        }
        var admin = ObjectSpace.FirstOrDefault<PermissionPolicyUser>(u => u.UserName == AdminUserName);
        if (admin == null) {
            admin = ObjectSpace.CreateObject<PermissionPolicyUser>();
            admin.UserName = AdminUserName;
            admin.SetPassword("");
            admin.Roles.Add(adminRole);
        }

        var mcpRole = ObjectSpace.FirstOrDefault<PermissionPolicyRole>(r => r.Name == McpRoleName);
        if (mcpRole == null) {
            mcpRole = ObjectSpace.CreateObject<PermissionPolicyRole>();
            mcpRole.Name = McpRoleName;
            // Read-only on the 8 domain types; everything else stays denied by default.
            mcpRole.SetTypePermission<Region>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<Customer>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<Person>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<Product>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<Order>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<OrderLine>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<Project>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<ProjectTask>(SecurityOperations.Read, SecurityPermissionState.Allow);
            // The security demo: HourlyRate never reaches the MCP client.
            mcpRole.AddMemberPermission<Person>(SecurityOperations.Read, nameof(Person.HourlyRate), null, SecurityPermissionState.Deny);
        }
        var mcpUser = ObjectSpace.FirstOrDefault<PermissionPolicyUser>(u => u.UserName == McpUserName);
        if (mcpUser == null) {
            mcpUser = ObjectSpace.CreateObject<PermissionPolicyUser>();
            mcpUser.UserName = McpUserName;
            mcpUser.SetPassword(McpDevPassword);
            mcpUser.Roles.Add(mcpRole);
        }
    }

    void SeedDemoData() {
        if (ObjectSpace.GetObjectsCount(typeof(Region), null) > 0) return; // idempotent

        var d = DemoDataGenerator.Generate(DateTime.Today);

        var regions = d.Regions.Select(r => {
            var e = ObjectSpace.CreateObject<Region>();
            e.Name = r.Name;
            return e;
        }).ToList();
        var regionByName = regions.ToDictionary(r => r.Name);

        var persons = d.Persons.Select(p => {
            var e = ObjectSpace.CreateObject<Person>();
            e.FirstName = p.First; e.LastName = p.Last; e.Email = p.Email; e.Phone = p.Phone; e.HourlyRate = p.HourlyRate;
            return e;
        }).ToList();

        var customers = d.Customers.Select(c => {
            var e = ObjectSpace.CreateObject<Customer>();
            e.Name = c.Name; e.City = c.City; e.Region = regionByName[c.Region];
            return e;
        }).ToList();

        var products = d.Products.Select(p => {
            var e = ObjectSpace.CreateObject<Product>();
            e.Name = p.Name; e.Category = p.Category; e.UnitPrice = p.UnitPrice; e.Active = true;
            return e;
        }).ToList();

        foreach (var o in d.Orders) {
            var e = ObjectSpace.CreateObject<Order>();
            e.Customer = customers[o.CustomerIndex]; e.OrderDate = o.OrderDate; e.Status = o.Status;
            foreach (var l in o.Lines) {
                var line = ObjectSpace.CreateObject<OrderLine>();
                line.Product = products[l.ProductIndex];
                line.Quantity = l.Quantity;
                line.UnitPrice = products[l.ProductIndex].UnitPrice;
                line.Discount = l.Discount;
                e.Lines.Add(line);
            }
        }

        var projects = d.Projects.Select(p => {
            var e = ObjectSpace.CreateObject<Project>();
            e.Name = p.Name; e.Customer = customers[p.CustomerIndex]; e.Manager = persons[p.ManagerIndex];
            e.StartDate = p.StartDate; e.DueDate = p.DueDate; e.Status = p.Status; e.Budget = p.Budget;
            return e;
        }).ToList();

        foreach (var t in d.ProjectTasks) {
            var e = ObjectSpace.CreateObject<ProjectTask>();
            e.Subject = t.Subject; e.Project = projects[t.ProjectIndex]; e.AssignedTo = persons[t.PersonIndex];
            e.Status = t.Status; e.DueDate = t.DueDate; e.EstimatedHours = t.EstimatedHours; e.ActualHours = t.ActualHours;
        }
    }
}
```

- [ ] **Step 2: Rebuild the database with seed**

```powershell
./scripts/stop-app.ps1
dotnet build XafMcp.sln
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('XafMcp') IS NOT NULL BEGIN ALTER DATABASE XafMcp SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE XafMcp; END"
dotnet run --project XafMcp.Blazor.Server -- --updateDatabase --forceUpdate --silent
```

Expected: exit 0. (Seeding ~500 orders takes a moment; `Order.OnSaving` computes `Total`.)

- [ ] **Step 3: Verify seed + security rows via SQL**

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -Q "SELECT (SELECT COUNT(*) FROM Regions) AS Regions, (SELECT COUNT(*) FROM Customers) AS Customers, (SELECT COUNT(*) FROM Orders) AS Orders, (SELECT COUNT(*) FROM OrderLines) AS OrderLines, (SELECT COUNT(*) FROM Projects) AS Projects, (SELECT COUNT(*) FROM ProjectTasks) AS Tasks"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -Q "SELECT UserName FROM Users; SELECT Name FROM Roles"
sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -Q "SELECT COUNT(*) AS TotalsComputed FROM Orders WHERE Total > 0"
```

Expected: Regions=5, Customers=30, Orders=500, OrderLines>500, Projects=6, Tasks=60; users `Admin` + `McpAgent`, roles `Administrators` + `McpReadOnly`; TotalsComputed ≈ 500 (a handful of zero-total orders is acceptable only if every line rolled Discount=0 and Quantity×Price=0 — which cannot happen, so expect 500). If table names differ (e.g. `Person` vs `Persons`), fix the DbSet property names in Task 2, not the SQL.

- [ ] **Step 4: Boot + login smoke by hand**

```powershell
./scripts/run-app.ps1
```

Log in at `http://localhost:5210` as `Admin` / empty password; confirm the nav shows Customers/Orders/Products/Projects with data. Then `./scripts/stop-app.ps1`.

- [ ] **Step 5: Commit**

```powershell
git add XafMcp.Module/DatabaseUpdate/Updater.cs
git commit -m "feat: security seed (Admin, McpAgent/McpReadOnly with HourlyRate member-deny) + demo data" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Serilog → console + CLEF rolling files

**Files:**
- Modify: `XafMcp.Blazor.Server/XafMcp.Blazor.Server.csproj` (packages)
- Modify: `XafMcp.Blazor.Server/Program.cs` (UseSerilog)

**Interfaces:**
- Consumes: nothing new
- Produces: CLEF files at `XafMcp.Blazor.Server/logs/xafmcp-YYYYMMDD.clef` — Task 12's `LogTools` resolves this directory as `Path.Combine(env.ContentRootPath, "logs")` and globs `xafmcp-*.clef`

- [ ] **Step 1: Packages**

Add to the Blazor.Server csproj `<ItemGroup>` with the other packages:

```xml
<PackageReference Include="Serilog.AspNetCore" Version="10.0.0" />
<PackageReference Include="Serilog.Formatting.Compact" Version="3.0.0" />
<PackageReference Include="Serilog.Sinks.File" Version="7.0.0" />
```

- [ ] **Step 2: Wire UseSerilog**

In `Program.cs`, add `using Serilog;` and `using Serilog.Formatting.Compact;`, and insert into `CreateHostBuilder` (before `.ConfigureWebHostDefaults`):

```csharp
.UseSerilog((context, configuration) => {
    configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(new CompactJsonFormatter(), "logs/xafmcp-.clef",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true);
        // NOTE: Serilog inserts the rolling date before the LAST extension, so the
        // template "xafmcp-.clef" yields files named xafmcp-20260809.clef. A
        // ".clef.json" template would yield "xafmcp-.clef20260809.json" and break
        // the Task 12 glob.
})
```

- [ ] **Step 3: Verify a parseable CLEF file appears**

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln
./scripts/run-app.ps1
./scripts/stop-app.ps1
$log = Get-ChildItem XafMcp.Blazor.Server\logs\xafmcp-*.clef | Sort-Object Name | Select-Object -Last 1
$first = Get-Content $log.FullName -TotalCount 1 | ConvertFrom-Json
if (-not $first.'@t') { throw 'first log line is not CLEF' } else { Write-Host "CLEF OK: $($first.'@t')" }
```

Expected: `CLEF OK: <timestamp>`.

- [ ] **Step 4: Commit**

```powershell
git add XafMcp.Blazor.Server/XafMcp.Blazor.Server.csproj XafMcp.Blazor.Server/Program.cs
git commit -m "feat: Serilog console + CLEF rolling file sink" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: MCP endpoint + get_server_info + scripts/mcp-call.ps1

**Files:**
- Modify: `XafMcp.Blazor.Server/XafMcp.Blazor.Server.csproj` (MCP package)
- Modify: `XafMcp.Blazor.Server/Startup.cs` (AddMcpServer + MapMcp)
- Create: `XafMcp.Blazor.Server/Mcp/JsonOpts.cs`, `Mcp/ServerInfoTools.cs`
- Create: `scripts/mcp-call.ps1`

**Interfaces:**
- Consumes: running app from Task 1/5
- Produces: `/mcp` Streamable-HTTP endpoint; `JsonOpts.Indented` (shared `JsonSerializerOptions`); `scripts/mcp-call.ps1 -Tool <name> -ArgsJson '<json>'` and `-List` — the verification vehicle for Tasks 7–12

- [ ] **Step 1: Package**

```powershell
dotnet add XafMcp.Blazor.Server package ModelContextProtocol.AspNetCore --prerelease
```

Whatever version resolves, leave it pinned exactly as `dotnet add` wrote it.

- [ ] **Step 2: Tool class + JSON options**

`XafMcp.Blazor.Server/Mcp/JsonOpts.cs`:

```csharp
using System.Text.Json;

namespace XafMcp.Blazor.Server.Mcp;

public static class JsonOpts {
    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
```

`XafMcp.Blazor.Server/Mcp/ServerInfoTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class ServerInfoTools(IConfiguration configuration) {
    static readonly DateTime StartedUtc = DateTime.UtcNow;

    [McpServerTool(Name = "get_server_info")]
    [Description("Application, DevExpress and .NET versions, uptime, database name, MCP identity and tool limits.")]
    public string GetServerInfo() {
        var cs = configuration.GetConnectionString("ConnectionString") ?? "";
        var info = new {
            application = "XafMcp",
            appVersion = typeof(ServerInfoTools).Assembly.GetName().Version?.ToString(),
            devExpressVersion = typeof(DevExpress.ExpressApp.XafApplication).Assembly.GetName().Version?.ToString(),
            dotnetVersion = Environment.Version.ToString(),
            uptime = (DateTime.UtcNow - StartedUtc).ToString(@"d\.hh\:mm\:ss"),
            database = new SqlConnectionStringBuilder(cs).InitialCatalog,
            mcpIdentity = "McpAgent",
            limits = new { queryTopMax = 500, aggregateGroupMax = 500 },
        };
        return JsonSerializer.Serialize(info, JsonOpts.Indented);
    }
}
```

- [ ] **Step 3: Startup wiring**

In `ConfigureServices`, after the `authentication.AddCookie(...)` block:

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<Mcp.ServerInfoTools>();
```

In `Configure`'s `UseEndpoints` block, add:

```csharp
endpoints.MapMcp("/mcp");
```

If `MapMcp`/`WithHttpTransport` names don't compile, the installed prerelease has drifted — check the exact method names in the package's README (`https://github.com/modelcontextprotocol/csharp-sdk`) before renaming anything else.

- [ ] **Step 4: mcp-call.ps1**

`scripts/mcp-call.ps1`:

```powershell
param(
    [string]$Tool,
    [string]$ArgsJson = '{}',
    [switch]$List,
    [string]$BaseUrl = 'http://localhost:5210/mcp'
)
$ErrorActionPreference = 'Stop'

function Parse-McpBody([string]$body) {
    # Streamable HTTP may answer application/json or an SSE frame ("event: message\ndata: {...}")
    $jsonLine = if ($body -match '(?m)^data: (.+)$') { $Matches[1] } else { $body }
    return $jsonLine | ConvertFrom-Json
}

$headers = @{ 'Accept' = 'application/json, text/event-stream' }

$init = @{ jsonrpc = '2.0'; id = 1; method = 'initialize'; params = @{
    protocolVersion = '2025-06-18'; capabilities = @{}; clientInfo = @{ name = 'mcp-call'; version = '1.0' } } } | ConvertTo-Json -Depth 8
$r = Invoke-WebRequest $BaseUrl -Method Post -Body $init -ContentType 'application/json' -Headers $headers -UseBasicParsing
if ($r.Headers['mcp-session-id']) { $headers['mcp-session-id'] = @($r.Headers['mcp-session-id'])[0] }

$initialized = @{ jsonrpc = '2.0'; method = 'notifications/initialized' } | ConvertTo-Json
Invoke-WebRequest $BaseUrl -Method Post -Body $initialized -ContentType 'application/json' -Headers $headers -UseBasicParsing | Out-Null

if ($List) {
    $req = @{ jsonrpc = '2.0'; id = 2; method = 'tools/list'; params = @{} } | ConvertTo-Json -Depth 4
    $resp = Parse-McpBody (Invoke-WebRequest $BaseUrl -Method Post -Body $req -ContentType 'application/json' -Headers $headers -UseBasicParsing).Content
    $resp.result.tools | ForEach-Object { $_.name }
    exit 0
}

$call = @{ jsonrpc = '2.0'; id = 2; method = 'tools/call'; params = @{
    name = $Tool; arguments = ($ArgsJson | ConvertFrom-Json) } } | ConvertTo-Json -Depth 16
$resp = Parse-McpBody (Invoke-WebRequest $BaseUrl -Method Post -Body $call -ContentType 'application/json' -Headers $headers -UseBasicParsing).Content
if ($resp.error) { Write-Error ("MCP error: " + ($resp.error | ConvertTo-Json -Depth 8)) }
if ($resp.result.isError) { Write-Error ("Tool error: " + (($resp.result.content | Where-Object type -eq 'text').text -join "`n")) }
($resp.result.content | Where-Object type -eq 'text').text
```

- [ ] **Step 5: Smoke**

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
./scripts/mcp-call.ps1 -List                 # expect: get_server_info
./scripts/mcp-call.ps1 -Tool get_server_info # expect JSON with devExpressVersion 26.1.4.0 and database XafMcp
./scripts/stop-app.ps1
```

- [ ] **Step 6: Commit**

```powershell
git add XafMcp.Blazor.Server/XafMcp.Blazor.Server.csproj XafMcp.Blazor.Server/Startup.cs XafMcp.Blazor.Server/Mcp scripts/mcp-call.ps1
git commit -m "feat: embedded Streamable-HTTP MCP endpoint with get_server_info" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: list_entities + describe_entity (metadata tools)

**Files:**
- Create: `XafMcp.Blazor.Server/Mcp/EntityRegistry.cs`, `Mcp/DataTools.cs`
- Modify: `XafMcp.Blazor.Server/Startup.cs` (`.WithTools<Mcp.DataTools>()`)

**Interfaces:**
- Consumes: entities (Task 2), MCP plumbing (Task 6)
- Produces: `EntityRegistry.DomainTypes` (the 8-type whitelist) and `EntityRegistry.Resolve(string) : ITypeInfo` — reused by Tasks 8–11. `DataTools` gains `query_entities`/`aggregate_entities` in Tasks 8–9; write it now with a constructor that takes no arguments (Task 8 adds the `McpSecurityContext` dependency).

- [ ] **Step 1: EntityRegistry**

`XafMcp.Blazor.Server/Mcp/EntityRegistry.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using ModelContextProtocol;
using XafMcp.Module.BusinessObjects;

namespace XafMcp.Blazor.Server.Mcp;

public static class EntityRegistry {
    // Whitelist: MCP exposes exactly the 8 domain entities — never security/system types.
    public static readonly Type[] DomainTypes = [
        typeof(Region), typeof(Customer), typeof(Person), typeof(Product),
        typeof(Order), typeof(OrderLine), typeof(Project), typeof(ProjectTask),
    ];

    public static ITypeInfo Resolve(string entity) {
        var type = DomainTypes.FirstOrDefault(t =>
            t.Name.Equals(entity, StringComparison.OrdinalIgnoreCase) ||
            t.FullName!.Equals(entity, StringComparison.OrdinalIgnoreCase));
        if (type == null) {
            throw new McpException($"Unknown entity '{entity}'. Valid entities: {string.Join(", ", DomainTypes.Select(t => t.Name))}");
        }
        return XafTypesInfo.Instance.FindTypeInfo(type);
    }
}
```

(If `McpException` lives elsewhere in the installed SDK version, follow the compiler — it is in the `ModelContextProtocol` namespace in current prereleases. A thrown `McpException` reaches the client as a tool error message the LLM can read and self-correct on.)

- [ ] **Step 2: DataTools with the two metadata tools**

`XafMcp.Blazor.Server/Mcp/DataTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using DevExpress.ExpressApp.DC;
using ModelContextProtocol.Server;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class DataTools {
    [McpServerTool(Name = "list_entities")]
    [Description("List the queryable business entities of this application. Start here; then use describe_entity for properties.")]
    public string ListEntities() {
        var entities = EntityRegistry.DomainTypes.Select(t => {
            var ti = XafMcp.Blazor.Server.Mcp.EntityRegistry.Resolve(t.Name);
            return new { name = t.Name, caption = ti.Type.Name, fullName = t.FullName };
        });
        return JsonSerializer.Serialize(new { entities }, JsonOpts.Indented);
    }

    [McpServerTool(Name = "describe_entity")]
    [Description("Property metadata for one entity: names, types, enum values, reference targets, key. Property names returned here are exactly what query_entities/aggregate_entities criteria and paths accept.")]
    public string DescribeEntity(
        [Description("Entity name from list_entities, e.g. 'Customer'")] string entity) {
        var ti = EntityRegistry.Resolve(entity);
        var properties = ti.Members
            .Where(m => m.IsPublic && m.IsPersistent && m.Name != "ID" || (m.IsList && m.IsPublic && m.IsPersistent))
            .Where(m => !m.Name.Contains("GCRecord") && !m.Name.EndsWith("Id"))
            .Select(m => new {
                name = m.Name,
                type = m.MemberTypeInfo?.IsEnum == true ? "enum" : SimpleTypeName(m.MemberType),
                enumValues = m.MemberTypeInfo?.IsEnum == true ? Enum.GetNames(m.MemberType) : null,
                isCollection = m.IsList,
                referencesEntity = !m.IsList && m.MemberTypeInfo?.IsPersistent == true ? m.MemberTypeInfo.Type.Name : null,
                nullable = m.MemberType.IsClass || Nullable.GetUnderlyingType(m.MemberType) != null,
            });
        return JsonSerializer.Serialize(new {
            entity = ti.Type.Name,
            key = ti.KeyMember?.Name,
            properties,
        }, JsonOpts.Indented);
    }

    static string SimpleTypeName(Type t) {
        t = Nullable.GetUnderlyingType(t) ?? t;
        if (t.IsGenericType) return t.Name; // collections keep their generic name
        return t.Name switch {
            "String" => "string", "Int32" => "int", "Decimal" => "decimal",
            "Boolean" => "bool", "DateTime" => "datetime", _ => t.Name,
        };
    }
}
```

- [ ] **Step 3: Register + smoke**

Startup: change the MCP registration to

```csharp
services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<Mcp.ServerInfoTools>()
    .WithTools<Mcp.DataTools>();
```

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
./scripts/mcp-call.ps1 -List   # expect: get_server_info, list_entities, describe_entity
./scripts/mcp-call.ps1 -Tool list_entities
./scripts/mcp-call.ps1 -Tool describe_entity -ArgsJson '{"entity":"Order"}'
./scripts/mcp-call.ps1 -Tool describe_entity -ArgsJson '{"entity":"Bogus"}' # expect tool error listing valid names
./scripts/stop-app.ps1
```

Expected: `Order` description shows `Customer` with `referencesEntity: Customer`, `Status` as enum with 4 values, `Total` decimal; the bogus call errors with the valid-entity list. Adjust the member filter if `RegionId`-style FK scalars or `GCRecord` leak through — the description should show model-level properties (`Region`), not storage scalars (`RegionId`).

- [ ] **Step 4: Commit**

```powershell
git add XafMcp.Blazor.Server/Mcp/EntityRegistry.cs XafMcp.Blazor.Server/Mcp/DataTools.cs XafMcp.Blazor.Server/Startup.cs
git commit -m "feat: list_entities + describe_entity metadata tools" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: McpSecurityContext + query_entities (secured, member-deny enforced)

**Files:**
- Create: `XafMcp.Blazor.Server/Mcp/McpSecurityContext.cs`, `Mcp/PermissionInspector.cs`, `Mcp/EntityProjector.cs`
- Modify: `XafMcp.Blazor.Server/Mcp/DataTools.cs` (constructor dependency + `query_entities`)
- Modify: `XafMcp.Blazor.Server/Startup.cs` (`services.AddScoped<Mcp.McpSecurityContext>();`)

**Interfaces:**
- Consumes: `McpAgent`/`McpReadOnly` seeded in Task 4; `EntityRegistry` from Task 7
- Produces: `McpSecurityContext.CreateObjectSpace(Type) : IObjectSpace` + consts `UserName`/`RoleName` (used by Tasks 9–10); `PermissionInspector.GetDeniedReadMembers(IObjectSpace, string roleName, Type) : HashSet<string>` (Tasks 9–10); `EntityProjector.Project(object, ITypeInfo, ISet<string>, string[]?) : Dictionary<string, object?>`

- [ ] **Step 1: McpSecurityContext** — the WLNHeadless `XafJobScopeInitializer` pattern (`SignInManager` needs an HTTP/circuit context; explicit `SecurityStrategyBase.Logon` is the only approach that works for a service identity):

`XafMcp.Blazor.Server/Mcp/McpSecurityContext.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.Core;
using DevExpress.ExpressApp.Security;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using ModelContextProtocol;

namespace XafMcp.Blazor.Server.Mcp;

/// <summary>
/// Scoped per MCP request. Authenticates the fixed McpAgent service user on the scope's
/// security strategy, then hands out SECURED object spaces. Every data-touching tool goes
/// through here — no non-secured access anywhere in the MCP path (spec §4).
/// </summary>
public sealed class McpSecurityContext(IServiceProvider serviceProvider, IConfiguration configuration, ILogger<McpSecurityContext> logger) {
    public const string UserName = "McpAgent";
    public const string RoleName = "McpReadOnly";

    bool authenticated;

    public IObjectSpace CreateObjectSpace(Type type) {
        EnsureAuthenticated();
        return serviceProvider.GetRequiredService<IObjectSpaceFactory>().CreateObjectSpace(type);
    }

    void EnsureAuthenticated() {
        if (authenticated) return;
        var securityStrategy = serviceProvider.GetRequiredService<ISecurityStrategyBase>();
        if (securityStrategy.IsAuthenticated) { authenticated = true; return; }
        if (securityStrategy is not SecurityStrategyBase strategyBase) {
            throw new McpException($"Unexpected security strategy '{securityStrategy.GetType().FullName}'.");
        }
        var nonSecuredFactory = serviceProvider.GetRequiredService<INonSecuredObjectSpaceFactory>();
        using var verifySpace = nonSecuredFactory.CreateNonSecuredObjectSpace<PermissionPolicyUser>();
        var userManager = serviceProvider.GetRequiredService<UserManager>();
        var user = userManager.FindUserByName<PermissionPolicyUser>(verifySpace, UserName);
        if (user is null) {
            throw new McpException($"Service user '{UserName}' not found — run the database updater (dotnet run -- --updateDatabase).");
        }
        var password = configuration["McpAgent:Password"] ?? string.Empty;
        var logonParams = new AuthenticationStandardLogonParameters(UserName, password);
        if (strategyBase is SecurityStrategy concreteStrategy) {
            concreteStrategy.Authentication.SetLogonParameters(logonParams);
        }
        using var logonSpace = nonSecuredFactory.CreateNonSecuredObjectSpace<PermissionPolicyUser>();
        strategyBase.Logon(logonSpace);
        logger.LogDebug("MCP scope authenticated as '{User}'", UserName);
        authenticated = true;
    }
}
```

- [ ] **Step 2: PermissionInspector** (property names `TargetType`/`MemberPermissions`/`Members`/`ReadState` verified against DX 26.1 installed sources — `PermissionsExtractor.cs`):

`XafMcp.Blazor.Server/Mcp/PermissionInspector.cs`:

```csharp
using DevExpress.ExpressApp;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;

namespace XafMcp.Blazor.Server.Mcp;

public static class PermissionInspector {
    /// <summary>Member names the given role may NOT read on the given type.</summary>
    public static HashSet<string> GetDeniedReadMembers(IObjectSpace space, string roleName, Type entityType) {
        var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var role = space.FirstOrDefault<PermissionPolicyRole>(r => r.Name == roleName);
        if (role is null) return denied;
        foreach (var tp in role.TypePermissions) {
            if (tp.TargetType != entityType) continue;
            foreach (var mp in tp.MemberPermissions) {
                if (mp.ReadState != SecurityPermissionState.Deny || string.IsNullOrEmpty(mp.Members)) continue;
                foreach (var member in mp.Members.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                    denied.Add(member);
                }
            }
        }
        return denied;
    }
}
```

- [ ] **Step 3: EntityProjector**

`XafMcp.Blazor.Server/Mcp/EntityProjector.cs`:

```csharp
using DevExpress.ExpressApp.DC;

namespace XafMcp.Blazor.Server.Mcp;

public static class EntityProjector {
    /// <summary>
    /// Projects an entity to a plain dictionary via IMemberInfo (never serialize EF proxies).
    /// Persistent scalar members + reference display text; denied members are omitted entirely.
    /// </summary>
    public static Dictionary<string, object?> Project(object obj, ITypeInfo ti, ISet<string> deniedMembers, string[]? properties) {
        var row = new Dictionary<string, object?>();
        foreach (var m in ti.Members) {
            if (!m.IsPublic || m.IsList || !m.IsPersistent) continue;
            if (m.Name.Contains("GCRecord") || m.Name == "OptimisticLockField") continue;
            if (m.Name.EndsWith("Id") && ti.FindMember(m.Name[..^2]) != null) continue; // hide FK scalar when the reference member exists
            if (deniedMembers.Contains(m.Name)) continue;
            if (properties is { Length: > 0 } && !properties.Contains(m.Name, StringComparer.OrdinalIgnoreCase)) continue;
            var value = m.GetValue(obj);
            row[m.Name] = value switch {
                null => null,
                Enum e => e.ToString(),
                DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
                decimal d => d,
                _ when m.MemberTypeInfo?.IsPersistent == true => DisplayText(value, m.MemberTypeInfo),
                _ => value,
            };
        }
        return row;
    }

    static string? DisplayText(object referenced, ITypeInfo refTi) {
        var displayMember = refTi.DefaultMember ?? refTi.KeyMember;
        return displayMember?.GetValue(referenced)?.ToString();
    }
}
```

- [ ] **Step 4: query_entities on DataTools**

Change `DataTools` to take the security context: `public sealed class DataTools(McpSecurityContext securityContext) { ... }` and add:

```csharp
[McpServerTool(Name = "query_entities")]
[Description("Query one entity through the secured object space. criteria uses XAF criteria syntax, e.g. \"Status = 'Shipped' And OrderDate > #2026-01-01#\" or \"Customer.Region.Name = 'North'\". Use describe_entity for valid property names.")]
public string QueryEntities(
    [Description("Entity name from list_entities")] string entity,
    [Description("XAF criteria string; omit for all rows")] string? criteria = null,
    [Description("Property name to sort by")] string? sort = null,
    [Description("Sort descending")] bool sortDescending = false,
    [Description("Max rows, default 50, cap 500")] int top = 50,
    [Description("Restrict output to these properties")] string[]? properties = null) {
    var ti = EntityRegistry.Resolve(entity);
    top = Math.Clamp(top, 1, 500);
    DevExpress.Data.Filtering.CriteriaOperator? crit = null;
    if (!string.IsNullOrWhiteSpace(criteria)) {
        try { crit = DevExpress.Data.Filtering.CriteriaOperator.Parse(criteria); }
        catch (Exception ex) {
            throw new ModelContextProtocol.McpException(
                $"Invalid criteria: {ex.Message}. XAF criteria syntax examples: \"Status = 'Shipped'\", \"OrderDate > #2026-01-01#\", \"Customer.Region.Name = 'North'\". Call describe_entity('{entity}') for valid property names.");
        }
    }
    var sorting = new List<DevExpress.Xpo.SortProperty>();
    if (!string.IsNullOrWhiteSpace(sort)) {
        sorting.Add(new DevExpress.Xpo.SortProperty(sort,
            sortDescending ? DevExpress.Xpo.SortingDirection.Descending : DevExpress.Xpo.SortingDirection.Ascending));
    }
    using var os = securityContext.CreateObjectSpace(ti.Type);
    var denied = PermissionInspector.GetDeniedReadMembers(os, McpSecurityContext.RoleName, ti.Type);
    var list = ((DevExpress.ExpressApp.EFCore.EFCoreObjectSpace)os).GetObjects(ti.Type, crit, sorting, false);
    // ponytail: top is applied after materialization — fine at POC row counts (≤500 orders);
    // switch to GetObjectsQuery<T> via MakeGenericMethod for server-side Take if datasets grow
    var rows = list.Cast<object>().Take(top)
        .Select(o => EntityProjector.Project(o, ti, denied, properties)).ToList();
    return System.Text.Json.JsonSerializer.Serialize(new { entity = ti.Type.Name, returned = rows.Count, rows }, JsonOpts.Indented);
}
```

Register the scoped service in `ConfigureServices` (before `AddMcpServer`): `services.AddScoped<Mcp.McpSecurityContext>();`

- [ ] **Step 5: Runtime verification — including the member-deny**

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
$people = ./scripts/mcp-call.ps1 -Tool query_entities -ArgsJson '{"entity":"Person","top":5}'
if ($people -match 'HourlyRate') { throw 'SECURITY LEAK: HourlyRate visible to McpAgent' } else { Write-Host 'HourlyRate correctly absent' }
./scripts/mcp-call.ps1 -Tool query_entities -ArgsJson '{"entity":"Order","criteria":"Status = ''Shipped''","sort":"OrderDate","sortDescending":true,"top":5}'
./scripts/mcp-call.ps1 -Tool query_entities -ArgsJson '{"entity":"Customer","criteria":"Region.Name = ''North''","top":5}'
try { ./scripts/mcp-call.ps1 -Tool query_entities -ArgsJson '{"entity":"Order","criteria":"Bogus ="}' } catch { Write-Host "criteria error surfaced OK: $_" }
./scripts/stop-app.ps1
```

Expected: Person rows contain FirstName/LastName/Email/Phone but **no HourlyRate key**; Shipped orders sorted newest-first with Customer as display text; North customers only; the bad criteria call errors with a parser message mentioning valid syntax.

- [ ] **Step 6: Commit**

```powershell
git add XafMcp.Blazor.Server/Mcp XafMcp.Blazor.Server/Startup.cs
git commit -m "feat: McpAgent secured object space + query_entities with member-deny projection" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: aggregate_entities + PathValueResolver (unit-tested)

**Files:**
- Create: `XafMcp.Module/Services/PathValueResolver.cs`
- Create: `XafMcp.Tests/PathValueResolverTests.cs`
- Modify: `XafMcp.Blazor.Server/Mcp/DataTools.cs` (add `aggregate_entities`)

**Interfaces:**
- Consumes: `McpSecurityContext`, `PermissionInspector`, `EntityRegistry` (Tasks 7–8)
- Produces: `PathValueResolver.GetValue(object root, string path) : object?` (reflection walk, case-insensitive, null-propagating, throws `ArgumentException` on unknown segment)

- [ ] **Step 1: Failing tests**

`XafMcp.Tests/PathValueResolverTests.cs`:

```csharp
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
        // Home must be non-null: null-propagation short-circuits before segment validation (Task 9 finding)
        var o = new Owner { Home = new Home() };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => PathValueResolver.GetValue(o, "Home.Street"));
        Assert.IsTrue(ex.Message.Contains("Street"));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test XafMcp.Tests --filter PathValueResolverTests` — Expected: FAIL (type not defined).

- [ ] **Step 3: Implement**

`XafMcp.Module/Services/PathValueResolver.cs`:

```csharp
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
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test XafMcp.Tests --filter PathValueResolverTests` — Expected: PASS (4/4).

- [ ] **Step 5: aggregate_entities on DataTools**

Add to `DataTools`:

```csharp
[McpServerTool(Name = "aggregate_entities")]
[Description("Group-and-aggregate one entity. group_by accepts property paths like 'Customer.Region.Name' or 'Status'. function: count | sum | avg | min | max (sum/avg/min/max need measure). Optional XAF criteria pre-filters rows.")]
public string AggregateEntities(
    [Description("Entity name from list_entities")] string entity,
    [Description("Group-by property path, e.g. 'Customer.Region.Name'")] string group_by,
    [Description("count | sum | avg | min | max")] string function = "count",
    [Description("Numeric property path to measure (required for sum/avg/min/max)")] string? measure = null,
    [Description("XAF criteria string to pre-filter rows")] string? criteria = null) {
    var ti = EntityRegistry.Resolve(entity);
    function = function.ToLowerInvariant();
    if (function is not ("count" or "sum" or "avg" or "min" or "max")) {
        throw new ModelContextProtocol.McpException("function must be one of: count, sum, avg, min, max");
    }
    if (function != "count" && string.IsNullOrWhiteSpace(measure)) {
        throw new ModelContextProtocol.McpException($"function '{function}' requires a measure property");
    }
    DevExpress.Data.Filtering.CriteriaOperator? crit = null;
    if (!string.IsNullOrWhiteSpace(criteria)) {
        try { crit = DevExpress.Data.Filtering.CriteriaOperator.Parse(criteria); }
        catch (Exception ex) { throw new ModelContextProtocol.McpException($"Invalid criteria: {ex.Message}"); }
    }
    using var os = securityContext.CreateObjectSpace(ti.Type);
    GuardPathAgainstDeniedMembers(os, ti, group_by);
    if (measure != null) GuardPathAgainstDeniedMembers(os, ti, measure);

    var list = ((DevExpress.ExpressApp.EFCore.EFCoreObjectSpace)os).GetObjects(ti.Type, crit, new List<DevExpress.Xpo.SortProperty>(), false);
    // ponytail: in-memory grouping — correct and simple at POC scale; move to a LINQ GroupBy over
    // GetObjectsQuery<T> if row counts grow past a few thousand
    var groups = list.Cast<object>()
        .GroupBy(o => XafMcp.Module.Services.PathValueResolver.GetValue(o, group_by)?.ToString() ?? "(null)")
        .Select(g => new {
            group = g.Key,
            count = g.Count(),
            value = function switch {
                "count" => (decimal?)g.Count(),
                "sum" => g.Sum(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                "avg" => g.Average(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                "min" => g.Min(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
                _ => g.Max(o => ToDecimal(XafMcp.Module.Services.PathValueResolver.GetValue(o, measure!))),
            },
        })
        .OrderByDescending(g => g.value)
        .Take(500)
        .ToList();
    return System.Text.Json.JsonSerializer.Serialize(new { entity = ti.Type.Name, group_by, function, measure, groups }, JsonOpts.Indented);
}

static decimal ToDecimal(object? value) => value is null ? 0m : Convert.ToDecimal(value);

void GuardPathAgainstDeniedMembers(DevExpress.ExpressApp.IObjectSpace os, DevExpress.ExpressApp.DC.ITypeInfo rootTi, string path) {
    // Walk the ITypeInfo chain alongside the path; refuse any segment the MCP role can't read.
    var currentTi = rootTi;
    foreach (var segment in path.Split('.')) {
        if (currentTi == null) break;
        var denied = PermissionInspector.GetDeniedReadMembers(os, McpSecurityContext.RoleName, currentTi.Type);
        if (denied.Contains(segment)) {
            throw new ModelContextProtocol.McpException($"Access to '{currentTi.Type.Name}.{segment}' is denied for the MCP role.");
        }
        currentTi = currentTi.FindMember(segment)?.MemberTypeInfo;
    }
}
```

- [ ] **Step 6: Runtime verification**

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
./scripts/mcp-call.ps1 -Tool aggregate_entities -ArgsJson '{"entity":"Order","group_by":"Customer.Region.Name","function":"sum","measure":"Total"}'
./scripts/mcp-call.ps1 -Tool aggregate_entities -ArgsJson '{"entity":"ProjectTask","group_by":"Status"}'
try { ./scripts/mcp-call.ps1 -Tool aggregate_entities -ArgsJson '{"entity":"Person","group_by":"LastName","function":"avg","measure":"HourlyRate"}' } catch { Write-Host "denied-measure blocked OK: $_" }
./scripts/stop-app.ps1
```

Expected: 5 region groups with non-uniform sums; 4 task-status groups; the HourlyRate aggregate is **refused** with a denial message.

- [ ] **Step 7: Commit**

```powershell
git add XafMcp.Module/Services/PathValueResolver.cs XafMcp.Tests/PathValueResolverTests.cs XafMcp.Blazor.Server/Mcp/DataTools.cs
git commit -m "feat: aggregate_entities with path grouping and denied-member guard" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Security-insight tools — list_roles + explain_permissions

**Files:**
- Create: `XafMcp.Blazor.Server/Mcp/SecurityTools.cs`
- Modify: `XafMcp.Blazor.Server/Startup.cs` (`.WithTools<Mcp.SecurityTools>()`)

**Interfaces:**
- Consumes: `McpSecurityContext` (Task 8); the EF PermissionPolicy traversal contract: `role.TypePermissions[*].{TargetType, ReadState, WriteState, CreateState, DeleteState, NavigateState}`, `.ObjectPermissions[*].{Criteria, ReadState, WriteState, DeleteState, NavigateState}`, `.MemberPermissions[*].{Members, ReadState, WriteState}` (verified against DX 26.1 sources)
- Produces: tools `list_roles`, `explain_permissions`

- [ ] **Step 1: SecurityTools**

`XafMcp.Blazor.Server/Mcp/SecurityTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF.PermissionPolicy;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class SecurityTools(McpSecurityContext securityContext) {

    [McpServerTool(Name = "list_roles")]
    [Description("All security roles with a per-type permission summary. 'R' = read; 'minus <member>' marks member-level read denies.")]
    public string ListRoles() {
        using var os = securityContext.CreateObjectSpace(typeof(PermissionPolicyRole));
        var roles = os.GetObjects<PermissionPolicyRole>().Select(role => new {
            name = role.Name,
            isAdministrative = role.IsAdministrative,
            userCount = role.Users.Count,
            typePermissions = role.TypePermissions
                .Where(tp => tp.TargetType != null)
                .Select(tp => Summarize(tp))
                .ToList(),
        }).ToList();
        return JsonSerializer.Serialize(new { roles }, JsonOpts.Indented);
    }

    static string Summarize(PermissionPolicyTypePermissionObject tp) {
        var ops = "";
        if (tp.ReadState == SecurityPermissionState.Allow) ops += "R";
        if (tp.WriteState == SecurityPermissionState.Allow) ops += "W";
        if (tp.CreateState == SecurityPermissionState.Allow) ops += "C";
        if (tp.DeleteState == SecurityPermissionState.Allow) ops += "D";
        var deniedMembers = tp.MemberPermissions
            .Where(mp => mp.ReadState == SecurityPermissionState.Deny && !string.IsNullOrEmpty(mp.Members))
            .SelectMany(mp => mp.Members!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        var suffix = deniedMembers.Count > 0 ? $" minus {string.Join(",", deniedMembers)}" : "";
        return $"{tp.TargetType!.Name}:{(ops.Length > 0 ? ops : "-")}{suffix}";
    }

    [McpServerTool(Name = "explain_permissions")]
    [Description("Full type/object/member permission breakdown for a role (default: the MCP agent's own role). Answers 'what can this identity see and why'.")]
    public string ExplainPermissions(
        [Description("Role name; omit for the MCP role")] string? role = null,
        [Description("Restrict output to one entity")] string? entity = null) {
        role ??= McpSecurityContext.RoleName;
        Type? entityFilter = entity != null ? EntityRegistry.Resolve(entity).Type : null;
        using var os = securityContext.CreateObjectSpace(typeof(PermissionPolicyRole));
        var roleObj = os.FirstOrDefault<PermissionPolicyRole>(r => r.Name == role)
            ?? throw new McpException($"Role '{role}' not found. Use list_roles for valid names.");
        var entityPermissions = roleObj.TypePermissions
            .Where(tp => tp.TargetType != null && (entityFilter == null || tp.TargetType == entityFilter))
            .Select(tp => new {
                entity = tp.TargetType!.Name,
                typeLevel = new {
                    read = State(tp.ReadState), write = State(tp.WriteState),
                    create = State(tp.CreateState), delete = State(tp.DeleteState), navigate = State(tp.NavigateState),
                },
                objectLevel = tp.ObjectPermissions.Select(op => new {
                    criteria = op.Criteria,
                    read = State(op.ReadState), write = State(op.WriteState), delete = State(op.DeleteState),
                }).ToList(),
                memberLevel = tp.MemberPermissions.Select(mp => new {
                    members = mp.Members,
                    read = State(mp.ReadState), write = State(mp.WriteState),
                }).ToList(),
            }).ToList();
        return JsonSerializer.Serialize(new {
            role = roleObj.Name,
            isAdministrative = roleObj.IsAdministrative,
            note = roleObj.IsAdministrative ? "administrative role bypasses permission checks" : "unlisted types are denied by default",
            entityPermissions,
        }, JsonOpts.Indented);
    }

    static string? State(SecurityPermissionState? state) => state?.ToString().ToLowerInvariant();
}
```

- [ ] **Step 2: Register + smoke**

Startup: add `.WithTools<Mcp.SecurityTools>()` to the MCP chain.

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
./scripts/mcp-call.ps1 -Tool list_roles
$explained = ./scripts/mcp-call.ps1 -Tool explain_permissions
if ($explained -match 'HourlyRate' -and $explained -match 'deny') { Write-Host 'member deny surfaced OK' } else { throw 'explain_permissions does not show the HourlyRate deny' }
./scripts/mcp-call.ps1 -Tool explain_permissions -ArgsJson '{"role":"Administrators"}'
./scripts/stop-app.ps1
```

Expected: `list_roles` shows `Administrators` (administrative, 1 user) and `McpReadOnly` with `Person:R minus HourlyRate`; default `explain_permissions` names the HourlyRate member deny; the Administrators call notes the bypass.

- [ ] **Step 3: Commit**

```powershell
git add XafMcp.Blazor.Server/Mcp/SecurityTools.cs XafMcp.Blazor.Server/Startup.cs
git commit -m "feat: list_roles + explain_permissions security-insight tools" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: Schema drift — comparer (pure, unit-tested) + check_schema_drift + drift-demo.sql

**Files:**
- Create: `XafMcp.Module/Schema/SchemaDriftComparer.cs`
- Create: `XafMcp.Tests/SchemaDriftComparerTests.cs`
- Create: `XafMcp.Blazor.Server/Mcp/SchemaTools.cs`
- Create: `docs/drift-demo.sql`
- Modify: `XafMcp.Blazor.Server/Startup.cs` (`.WithTools<Mcp.SchemaTools>()`)

**Interfaces:**
- Consumes: `XafMcpEFCoreDbContext` (model side), raw connection string from config (DB side)
- Produces: `SchemaDriftComparer.Compare(IReadOnlyList<ColumnInfo> model, IReadOnlyList<ColumnInfo> db) : List<DriftFinding>` with `ColumnInfo(string Table, string Column, string StoreType, bool IsNullable)` and `DriftFinding(string Kind, string Table, string? Column, string? ModelValue, string? DbValue, string Severity)`; kinds: `table_only_in_db`, `column_only_in_db`, `column_missing_in_db`, `type_mismatch`, `nullability_mismatch`

- [ ] **Step 1: Failing comparer tests**

`XafMcp.Tests/SchemaDriftComparerTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run to verify failure** — `dotnet test XafMcp.Tests --filter SchemaDriftComparerTests` → FAIL (types missing).

- [ ] **Step 3: Implement the comparer**

`XafMcp.Module/Schema/SchemaDriftComparer.cs`:

```csharp
namespace XafMcp.Module.Schema;

public sealed record ColumnInfo(string Table, string Column, string StoreType, bool IsNullable);
public sealed record DriftFinding(string Kind, string Table, string? Column, string? ModelValue, string? DbValue, string Severity);

public static class SchemaDriftComparer {
    /// <summary>Pure comparison of EF relational model columns vs INFORMATION_SCHEMA columns.
    /// "Model missing in DB" is an error (EF will fail at runtime); the rest are warnings.</summary>
    public static List<DriftFinding> Compare(IReadOnlyList<ColumnInfo> model, IReadOnlyList<ColumnInfo> db) {
        var findings = new List<DriftFinding>();
        var cmp = StringComparer.OrdinalIgnoreCase;
        var modelTables = model.Select(c => c.Table).Distinct(cmp).ToHashSet(cmp);
        var dbTables = db.Select(c => c.Table).Distinct(cmp).ToHashSet(cmp);

        foreach (var table in dbTables.Where(t => !modelTables.Contains(t)).OrderBy(t => t, cmp)) {
            findings.Add(new DriftFinding("table_only_in_db", table, null, null, null, "warning"));
        }

        var dbByKey = db.ToDictionary(c => (c.Table.ToLowerInvariant(), c.Column.ToLowerInvariant()));
        var modelByKey = model.ToDictionary(c => (c.Table.ToLowerInvariant(), c.Column.ToLowerInvariant()));

        foreach (var m in model) {
            if (!dbByKey.TryGetValue((m.Table.ToLowerInvariant(), m.Column.ToLowerInvariant()), out var d)) {
                findings.Add(new DriftFinding("column_missing_in_db", m.Table, m.Column, m.StoreType, null, "error"));
                continue;
            }
            if (!string.Equals(Normalize(m.StoreType), Normalize(d.StoreType), StringComparison.OrdinalIgnoreCase)) {
                findings.Add(new DriftFinding("type_mismatch", m.Table, m.Column, m.StoreType, d.StoreType, "warning"));
            }
            if (m.IsNullable != d.IsNullable) {
                findings.Add(new DriftFinding("nullability_mismatch", m.Table, m.Column,
                    m.IsNullable ? "NULL" : "NOT NULL", d.IsNullable ? "NULL" : "NOT NULL", "warning"));
            }
        }

        foreach (var d in db) {
            if (!dbTables.Contains(d.Table) || !modelTables.Contains(d.Table)) continue;
            if (!modelByKey.ContainsKey((d.Table.ToLowerInvariant(), d.Column.ToLowerInvariant()))) {
                findings.Add(new DriftFinding("column_only_in_db", d.Table, d.Column, null, d.StoreType, "warning"));
            }
        }
        return findings;
    }

    static string Normalize(string storeType) => storeType.Replace(" ", "").ToLowerInvariant();
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test XafMcp.Tests --filter SchemaDriftComparerTests` → PASS (6/6).

- [ ] **Step 5: SchemaTools (model + INFORMATION_SCHEMA readers)**

`XafMcp.Blazor.Server/Mcp/SchemaTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using XafMcp.Module.BusinessObjects;
using XafMcp.Module.Schema;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class SchemaTools(IConfiguration configuration) {

    [McpServerTool(Name = "check_schema_drift")]
    [Description("Compare the EF Core model against the live database schema (INFORMATION_SCHEMA). Reports extra/missing tables and columns, type, length, precision and nullability mismatches.")]
    public string CheckSchemaDrift() {
        var connectionString = configuration.GetConnectionString("ConnectionString")
            ?? throw new ModelContextProtocol.McpException("No connection string configured");
        var findings = SchemaDriftComparer.Compare(ReadModelColumns(connectionString), ReadDatabaseColumns(connectionString));
        return JsonSerializer.Serialize(new {
            status = findings.Count == 0 ? "in_sync" : "drift_detected",
            findingCount = findings.Count,
            findings,
        }, JsonOpts.Indented);
    }

    static List<ColumnInfo> ReadModelColumns(string connectionString) {
        // Schema work is deliberately outside XAF/security: a plain DbContext over the same model.
        var options = new DbContextOptionsBuilder<XafMcpEFCoreDbContext>().UseSqlServer(connectionString).Options;
        using var ctx = new XafMcpEFCoreDbContext(options);
        var result = new List<ColumnInfo>();
        foreach (var table in ctx.Model.GetRelationalModel().Tables) {
            foreach (var column in table.Columns) {
                result.Add(new ColumnInfo(table.Name, column.Name, column.StoreType, column.IsNullable));
            }
        }
        return result;
    }

    static List<ColumnInfo> ReadDatabaseColumns(string connectionString) {
        var result = new List<ColumnInfo>();
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 30;
        command.CommandText = """
            SELECT c.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.CHARACTER_MAXIMUM_LENGTH,
                   c.NUMERIC_PRECISION, c.NUMERIC_SCALE, c.DATETIME_PRECISION, c.IS_NULLABLE
            FROM INFORMATION_SCHEMA.COLUMNS c
            JOIN INFORMATION_SCHEMA.TABLES t
              ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE t.TABLE_TYPE = 'BASE TABLE' AND c.TABLE_SCHEMA = 'dbo'
            ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read()) {
            var dataType = reader.GetString(2);
            var maxLength = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
            var precision = reader.IsDBNull(4) ? (byte?)null : reader.GetByte(4);
            var scale = reader.IsDBNull(5) ? (int?)null : reader.GetInt32(5);
            var datetimePrecision = reader.IsDBNull(6) ? (short?)null : reader.GetInt16(6);
            result.Add(new ColumnInfo(
                reader.GetString(0), reader.GetString(1),
                ComposeStoreType(dataType, maxLength, precision, scale, datetimePrecision),
                reader.GetString(7) == "YES"));
        }
        return result;
    }

    // Mirrors EF SqlServer store-type strings so the pure comparer sees the same vocabulary.
    static string ComposeStoreType(string dataType, int? maxLength, byte? precision, int? scale, short? datetimePrecision) =>
        dataType switch {
            "nvarchar" or "varchar" or "nchar" or "char" or "varbinary" or "binary" =>
                $"{dataType}({(maxLength == -1 ? "max" : maxLength?.ToString() ?? "max")})",
            "decimal" or "numeric" => $"decimal({precision},{scale})",
            "datetime2" or "time" or "datetimeoffset" when datetimePrecision.HasValue && datetimePrecision != 7 =>
                $"{dataType}({datetimePrecision})",
            _ => dataType,
        };
}
```

Note: if `GetByte`/`GetInt16`/`GetInt32` throw `InvalidCastException` on your SQL Server version's INFORMATION_SCHEMA column types, read via `reader.GetValue(n)` + `Convert.ToInt32(...)` — the metadata types differ between numeric columns (`tinyint`/`smallint`/`int`). That is the expected fix, not a redesign.

Startup: add `.WithTools<Mcp.SchemaTools>()`.

- [ ] **Step 6: drift-demo.sql**

`docs/drift-demo.sql`:

```sql
-- XafMcp drift demo — deliberate, EF-read-compatible schema drift (spec §3 Schema).
-- Apply:  sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -i docs\drift-demo.sql
-- Undo:   drop & recreate the database (see README).

-- 1. Column the model does not know
ALTER TABLE dbo.Customers ADD LegacyCode nvarchar(20) NULL;

-- 2. Widen + nullability flip on a model column (model: nvarchar(100) NOT NULL)
ALTER TABLE dbo.Regions ALTER COLUMN Name nvarchar(200) NULL;

-- 3. A whole table the model does not know
CREATE TABLE dbo.LegacyImport (Id int NOT NULL PRIMARY KEY, Payload nvarchar(max) NULL);
```

- [ ] **Step 7: Runtime verification**

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
./scripts/mcp-call.ps1 -Tool check_schema_drift          # expect status in_sync (or note what drifts out of the box and why)
sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -i docs\drift-demo.sql
$drift = ./scripts/mcp-call.ps1 -Tool check_schema_drift
foreach ($needle in 'LegacyCode','LegacyImport','nullability_mismatch','type_mismatch') {
    if ($drift -notmatch $needle) { throw "drift output missing $needle" }
}
Write-Host 'drift findings OK'
./scripts/stop-app.ps1
```

If the pre-drift call is not `in_sync`, inspect the findings: systematic false positives (e.g. every `datetime2` or every `nvarchar(max)`) mean `ComposeStoreType` needs to match EF's vocabulary for that type — fix the composer, don't suppress findings.

- [ ] **Step 8: Commit**

```powershell
git add XafMcp.Module/Schema XafMcp.Tests/SchemaDriftComparerTests.cs XafMcp.Blazor.Server/Mcp/SchemaTools.cs XafMcp.Blazor.Server/Startup.cs docs/drift-demo.sql
git commit -m "feat: check_schema_drift with unit-tested comparer and drift demo script" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 12: Log forensics — ClefParser (unit-tested) + search_logs + summarize_logs

**Files:**
- Create: `XafMcp.Module/Logs/ClefParser.cs`
- Create: `XafMcp.Tests/ClefParserTests.cs`
- Create: `XafMcp.Blazor.Server/Mcp/LogTools.cs`
- Modify: `XafMcp.Blazor.Server/Startup.cs` (`.WithTools<Mcp.LogTools>()`)

**Interfaces:**
- Consumes: CLEF files from Task 5 (`logs/xafmcp-*.clef` under ContentRootPath)
- Produces: `ClefParser.ParseLine(string) : ClefEvent?` and `ClefParser.ParseFiles(IEnumerable<string>) : IEnumerable<ClefEvent>` with `ClefEvent(DateTimeOffset Timestamp, string Level, string Message, string? Exception, string? SourceContext)`

- [ ] **Step 1: Failing parser tests**

`XafMcp.Tests/ClefParserTests.cs`:

```csharp
using XafMcp.Module.Logs;

namespace XafMcp.Tests;

[TestClass]
public sealed class ClefParserTests {
    [TestMethod]
    public void Parses_full_event() {
        var line = """{"@t":"2026-08-09T10:15:30.1234567Z","@l":"Error","@mt":"Boom {Name}","@x":"System.InvalidOperationException: nope","SourceContext":"XafMcp.Startup","Name":"x"}""";
        var e = ClefParser.ParseLine(line);
        Assert.IsNotNull(e);
        Assert.IsTrue(e.Level == "Error");
        Assert.IsTrue(e.Message.Contains("Boom"));
        Assert.IsTrue(e.Exception!.Contains("InvalidOperationException"));
        Assert.IsTrue(e.SourceContext == "XafMcp.Startup");
        Assert.IsTrue(e.Timestamp.UtcDateTime.Hour == 10);
    }

    [TestMethod]
    public void Level_defaults_to_Information() {
        var e = ClefParser.ParseLine("""{"@t":"2026-08-09T10:15:30Z","@mt":"hello"}""");
        Assert.IsNotNull(e);
        Assert.IsTrue(e.Level == "Information");
    }

    [TestMethod]
    public void Malformed_lines_yield_null() {
        Assert.IsNull(ClefParser.ParseLine("not json at all"));
        Assert.IsNull(ClefParser.ParseLine("""{"no_timestamp":true}"""));
        Assert.IsNull(ClefParser.ParseLine(""));
    }

    [TestMethod]
    public void ParseFiles_skips_malformed_lines() {
        var tmp = Path.Combine(Path.GetTempPath(), $"clef-{Guid.NewGuid():N}.json");
        File.WriteAllLines(tmp, [
            """{"@t":"2026-08-09T10:00:00Z","@mt":"one"}""",
            "garbage",
            """{"@t":"2026-08-09T11:00:00Z","@l":"Warning","@mt":"two"}""",
        ]);
        try {
            var events = ClefParser.ParseFiles([tmp]).ToList();
            Assert.HasCount(2, events);
            Assert.IsTrue(events[1].Level == "Warning");
        } finally { File.Delete(tmp); }
    }
}
```

- [ ] **Step 2: Run to verify failure** — `dotnet test XafMcp.Tests --filter ClefParserTests` → FAIL.

- [ ] **Step 3: Implement the parser**

`XafMcp.Module/Logs/ClefParser.cs`:

```csharp
using System.Text.Json;

namespace XafMcp.Module.Logs;

public sealed record ClefEvent(DateTimeOffset Timestamp, string Level, string Message, string? Exception, string? SourceContext);

public static class ClefParser {
    /// <summary>One CLEF (Compact Log Event Format) line → event, or null when malformed.
    /// "@m" is preferred; "@mt" (message template, un-rendered) is the usual CLEF field.</summary>
    public static ClefEvent? ParseLine(string line) {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("@t", out var t) || !t.TryGetDateTimeOffset(out var timestamp)) return null;
            var level = root.TryGetProperty("@l", out var l) ? l.GetString() ?? "Information" : "Information";
            var message = root.TryGetProperty("@m", out var m) ? m.GetString()
                : root.TryGetProperty("@mt", out var mt) ? mt.GetString() : null;
            var exception = root.TryGetProperty("@x", out var x) ? x.GetString() : null;
            var source = root.TryGetProperty("SourceContext", out var sc) ? sc.GetString() : null;
            return new ClefEvent(timestamp, level, message ?? "", exception, source);
        }
        catch (JsonException) { return null; }
    }

    public static IEnumerable<ClefEvent> ParseFiles(IEnumerable<string> paths) {
        foreach (var path in paths.OrderBy(p => p)) {
            // FileShare.ReadWrite: Serilog holds the current file open (shared: true)
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = reader.ReadLine()) != null) {
                var parsed = ParseLine(line);
                if (parsed != null) yield return parsed;
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify pass** — `dotnet test XafMcp.Tests --filter ClefParserTests` → PASS (4/4).

- [ ] **Step 5: LogTools**

`XafMcp.Blazor.Server/Mcp/LogTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using XafMcp.Module.Logs;

namespace XafMcp.Blazor.Server.Mcp;

[McpServerToolType]
public sealed class LogTools(IWebHostEnvironment environment) {
    static readonly string[] LevelOrder = ["Verbose", "Debug", "Information", "Warning", "Error", "Fatal"];

    IEnumerable<ClefEvent> Load(DateTime? from, DateTime? to) {
        var dir = Path.Combine(environment.ContentRootPath, "logs");
        if (!Directory.Exists(dir)) return [];
        var files = Directory.GetFiles(dir, "xafmcp-*.clef");
        var events = ClefParser.ParseFiles(files);
        if (from.HasValue) events = events.Where(e => e.Timestamp >= from.Value);
        if (to.HasValue) events = events.Where(e => e.Timestamp <= to.Value);
        return events;
    }

    [McpServerTool(Name = "search_logs")]
    [Description("Search the application's Serilog CLEF files. Times are ISO 8601 (e.g. 2026-08-01T00:00:00Z). min_level: Verbose|Debug|Information|Warning|Error|Fatal.")]
    public string SearchLogs(
        [Description("Only events at/after this time")] DateTime? from = null,
        [Description("Only events at/before this time")] DateTime? to = null,
        [Description("Minimum level, default Information")] string min_level = "Information",
        [Description("Substring to match in message/exception/source")] string? contains = null,
        [Description(".NET regex to match instead of substring")] string? regex = null,
        [Description("Max events, default 100, cap 500")] int top = 100) {
        top = Math.Clamp(top, 1, 500);
        int minIndex = Array.FindIndex(LevelOrder, l => l.Equals(min_level, StringComparison.OrdinalIgnoreCase));
        if (minIndex < 0) throw new ModelContextProtocol.McpException($"Unknown level '{min_level}'. Valid: {string.Join("|", LevelOrder)}");
        Regex? rx = null;
        if (regex != null) {
            try { rx = new Regex(regex, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex) { throw new ModelContextProtocol.McpException($"Invalid regex: {ex.Message}"); }
        }
        bool Matches(ClefEvent e) {
            var haystack = $"{e.Message}\n{e.Exception}\n{e.SourceContext}";
            if (contains != null && !haystack.Contains(contains, StringComparison.OrdinalIgnoreCase)) return false;
            if (rx != null && !rx.IsMatch(haystack)) return false;
            return true;
        }
        var events = Load(from, to)
            .Where(e => Array.IndexOf(LevelOrder, e.Level) >= minIndex && Matches(e))
            .OrderByDescending(e => e.Timestamp)
            .Take(top)
            .Select(e => new {
                timestamp = e.Timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                level = e.Level,
                message = e.Message,
                exception = e.Exception,
                source = e.SourceContext,
            })
            .ToList();
        return JsonSerializer.Serialize(new { returned = events.Count, events }, JsonOpts.Indented);
    }

    [McpServerTool(Name = "summarize_logs")]
    [Description("Aggregate view of the log files: counts per level, top exception types, top sources. Start here for 'any errors lately?'.")]
    public string SummarizeLogs(
        [Description("Only events at/after this time")] DateTime? from = null,
        [Description("Only events at/before this time")] DateTime? to = null) {
        var events = Load(from, to).ToList();
        var byLevel = events.GroupBy(e => e.Level).OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());
        var topExceptions = events.Where(e => !string.IsNullOrEmpty(e.Exception))
            .GroupBy(e => e.Exception!.Split(':')[0].Split('\n')[0].Trim())
            .OrderByDescending(g => g.Count()).Take(5)
            .Select(g => new { exceptionType = g.Key, count = g.Count() }).ToList();
        var topSources = events.Where(e => e.SourceContext != null)
            .GroupBy(e => e.SourceContext!)
            .OrderByDescending(g => g.Count()).Take(5)
            .Select(g => new { source = g.Key, count = g.Count() }).ToList();
        return JsonSerializer.Serialize(new {
            totalEvents = events.Count,
            firstEvent = events.Count > 0 ? events.Min(e => e.Timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") : null,
            lastEvent = events.Count > 0 ? events.Max(e => e.Timestamp).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") : null,
            byLevel, topExceptions, topSources,
        }, JsonOpts.Indented);
    }
}
```

Startup: add `.WithTools<Mcp.LogTools>()`.

- [ ] **Step 6: Runtime verification**

```powershell
./scripts/stop-app.ps1; dotnet build XafMcp.sln; ./scripts/run-app.ps1
$summary = ./scripts/mcp-call.ps1 -Tool summarize_logs
if ($summary -notmatch 'Information') { throw 'no Information events summarized' }
./scripts/mcp-call.ps1 -Tool search_logs -ArgsJson '{"contains":"XAF","top":5}'
./scripts/mcp-call.ps1 -Tool search_logs -ArgsJson '{"min_level":"Warning","top":10}'
./scripts/stop-app.ps1
```

Expected: summary with non-zero totals and level counts; searches return events (Warning search may legitimately return 0 on a clean boot — `returned: 0` is a pass there).

- [ ] **Step 7: Commit**

```powershell
git add XafMcp.Module/Logs XafMcp.Tests/ClefParserTests.cs XafMcp.Blazor.Server/Mcp/LogTools.cs XafMcp.Blazor.Server/Startup.cs
git commit -m "feat: search_logs + summarize_logs over Serilog CLEF files" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 13: E2E project (Playwright login + MCP HTTP smoke), README, acceptance run

**Files:**
- Create: `XafMcp.E2E/XafMcp.E2E.csproj`, `XafMcp.E2E/LoginSmokeTests.cs`, `XafMcp.E2E/McpHttpSmokeTests.cs`
- Rewrite: `README.md` (run instructions + acceptance script)

**Interfaces:**
- Consumes: everything. The E2E tests require the app running (`scripts/run-app.ps1`) and `Assert.Ignore` when it is not — they never silently pass.

- [ ] **Step 1: E2E project**

`XafMcp.E2E/XafMcp.E2E.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.1.0" />
    <PackageReference Include="Microsoft.Playwright.NUnit" Version="1.55.0" />
    <PackageReference Include="NUnit" Version="4.4.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="5.1.0" />
  </ItemGroup>
</Project>
```

(If NuGet reports these exact versions unavailable, take the closest current stable — these four packages are all actively published.)

- [ ] **Step 2: Login smoke (per the xaf-playwright-testing pattern)**

`XafMcp.E2E/LoginSmokeTests.cs`:

```csharp
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace XafMcp.E2E;

[TestFixture]
public class LoginSmokeTests : PageTest {
    const string BaseUrl = "http://localhost:5210";

    static async Task<bool> AppIsUp() {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try { return (await http.GetAsync($"{BaseUrl}/LoginPage")).IsSuccessStatusCode; }
        catch { return false; }
    }

    [Test]
    public async Task Admin_can_log_in_and_sees_navigation() {
        if (!await AppIsUp()) Assert.Ignore("App not running — start it with scripts/run-app.ps1 first.");
        await Page.GotoAsync($"{BaseUrl}/LoginPage", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        // XAF Blazor login: first text input = user name, password stays empty for the dev Admin.
        var userName = Page.Locator("input[type='text']").First;
        await userName.WaitForAsync(new() { Timeout = 15000 });
        await userName.FillAsync("Admin");
        await Page.Locator("button[type='submit'], .dxbl-btn-primary button, button:has-text('Log In')").First.ClickAsync();
        // Landed in the app: navigation shows a domain item.
        await Expect(Page.Locator("text=Customers").First).ToBeVisibleAsync(new() { Timeout = 20000 });
        await Page.ScreenshotAsync(new() { Path = "login-smoke.png", FullPage = true });
    }
}
```

Note for the implementer: XAF Blazor login-page selectors vary by theme — if the submit selector misses, screenshot the page (`await Page.ScreenshotAsync(...)` before the click) and adjust to what is actually rendered. Keep the localization-resilient pattern (structural selectors first, text last).

- [ ] **Step 3: MCP HTTP smoke**

`XafMcp.E2E/McpHttpSmokeTests.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace XafMcp.E2E;

[TestFixture]
public class McpHttpSmokeTests {
    const string McpUrl = "http://localhost:5210/mcp";
    static readonly string[] ExpectedTools = [
        "get_server_info", "list_entities", "describe_entity", "query_entities", "aggregate_entities",
        "list_roles", "explain_permissions", "check_schema_drift", "search_logs", "summarize_logs",
    ];

    HttpClient http = null!;
    string? sessionId;

    [SetUp]
    public void SetUp() {
        http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("Accept", "application/json, text/event-stream");
    }

    [TearDown]
    public void TearDown() => http.Dispose();

    async Task<JsonElement?> Post(object payload, bool expectBody = true) {
        var request = new HttpRequestMessage(HttpMethod.Post, McpUrl) {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        if (sessionId != null) request.Headers.Add("mcp-session-id", sessionId);
        HttpResponseMessage response;
        try { response = await http.SendAsync(request); }
        catch (HttpRequestException) { Assert.Ignore("App not running — start it with scripts/run-app.ps1 first."); return null; }
        if (response.Headers.TryGetValues("mcp-session-id", out var values)) sessionId = values.First();
        if (!expectBody) return null;
        var body = await response.Content.ReadAsStringAsync();
        var dataLine = body.Split('\n').FirstOrDefault(l => l.StartsWith("data: "));
        var json = dataLine != null ? dataLine["data: ".Length..] : body;
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    async Task Initialize() {
        await Post(new {
            jsonrpc = "2.0", id = 1, method = "initialize",
            @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "e2e", version = "1.0" } },
        });
        await Post(new { jsonrpc = "2.0", method = "notifications/initialized" }, expectBody: false);
    }

    [Test]
    public async Task All_ten_tools_are_listed() {
        await Initialize();
        var response = await Post(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        var names = response!.Value.GetProperty("result").GetProperty("tools").EnumerateArray()
            .Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.That(names, Is.SupersetOf(ExpectedTools));
    }

    [Test]
    public async Task Query_returns_rows_and_never_hourly_rate() {
        await Initialize();
        var response = await Post(new {
            jsonrpc = "2.0", id = 2, method = "tools/call",
            @params = new { name = "query_entities", arguments = new { entity = "Person", top = 5 } },
        });
        var text = response!.Value.GetProperty("result").GetProperty("content").EnumerateArray()
            .First(c => c.GetProperty("type").GetString() == "text").GetProperty("text").GetString()!;
        Assert.That(text, Does.Contain("FirstName"));
        Assert.That(text, Does.Not.Contain("HourlyRate"));
    }
}
```

- [ ] **Step 4: Build + install browsers + run E2E against the running app**

```powershell
dotnet sln add XafMcp.E2E
./scripts/stop-app.ps1; dotnet build XafMcp.sln
pwsh XafMcp.E2E/bin/Debug/net10.0/playwright.ps1 install chromium
./scripts/run-app.ps1
dotnet test XafMcp.E2E
./scripts/stop-app.ps1
```

Expected: 3/3 pass (none ignored — the app is up). Inspect `login-smoke.png` visually: XAF shell with navigation, no error page.

- [ ] **Step 5: Unit suite still green**

Run: `dotnet test XafMcp.Tests` — Expected: all pass.

- [ ] **Step 6: README rewrite**

Replace `README.md` with:

```markdown
# XafMcp — an XAF LOB app as an MCP server

A DevExpress XAF Blazor Server application (Customers/Orders/Products/Projects/Tasks on LocalDB)
that embeds a Streamable-HTTP MCP endpoint in the same process, so an LLM client (Claude Code)
can interrogate the living application. Design: `docs/superpowers/specs/2026-08-09-xafmcp-design.md`.

## Run

    dotnet run --project XafMcp.Blazor.Server -- --updateDatabase --forceUpdate --silent   # first time: create + seed DB
    ./scripts/run-app.ps1        # app + /mcp on http://localhost:5210 (stop: ./scripts/stop-app.ps1)

UI login: `Admin`, empty password. MCP data identity: `McpAgent` (read-only role, `Person.HourlyRate` denied).

## Connect Claude Code

    claude mcp add --transport http xafmcp http://localhost:5210/mcp

## Tools (all read-only)

get_server_info · list_entities · describe_entity · query_entities · aggregate_entities ·
list_roles · explain_permissions · check_schema_drift · search_logs · summarize_logs

Ad-hoc calls without an LLM: `./scripts/mcp-call.ps1 -Tool query_entities -ArgsJson '{"entity":"Order","top":5}'` (or `-List`).

## Acceptance script (ask Claude Code, app running)

1. "Using the xafmcp tools, write a short report on product opportunities per region."
   → uses describe/aggregate/query; the seeded bias (Software→North, Services growing in South) must show up.
2. "Give me a project status report — include overdue tasks."
3. "Any errors or warnings in the app logs?" → summarize_logs / search_logs.
4. Apply drift first: `sqlcmd -S "(localdb)\MSSQLLocalDB" -d XafMcp -i docs\drift-demo.sql`,
   then: "Is the database schema in sync with the application model?" → LegacyCode/LegacyImport findings.
5. "What is the MCP agent allowed to see and why?" → explain_permissions names the HourlyRate deny.
6. In none of the answers may a `HourlyRate` value appear.

## Reset the database

    ./scripts/stop-app.ps1
    sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "ALTER DATABASE XafMcp SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE XafMcp"
    dotnet run --project XafMcp.Blazor.Server -- --updateDatabase --forceUpdate --silent

## Tests

    dotnet test XafMcp.Tests   # unit (pure logic; no app needed)
    dotnet test XafMcp.E2E     # login + MCP smoke (app must be running, else tests are Ignored)

Stack: .NET 10 · DevExpress XAF 26.1.4 (EF Core) · SQL Server LocalDB · ModelContextProtocol.AspNetCore · Serilog CLEF.
DevExpress license required. Private project.
```

- [ ] **Step 7: Final full-suite verification**

```powershell
./scripts/stop-app.ps1
dotnet build XafMcp.sln
dotnet test XafMcp.Tests
./scripts/run-app.ps1
dotnet test XafMcp.E2E
./scripts/mcp-call.ps1 -List    # 10 tools
./scripts/stop-app.ps1
```

Expected: build green, all unit + E2E tests pass, 10 tools listed.

- [ ] **Step 8: Commit**

```powershell
git add XafMcp.sln XafMcp.E2E README.md
git commit -m "feat: E2E login + MCP smoke tests; README with acceptance script" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 9: Human acceptance (not automatable)**

Ask the user to run the README acceptance script with Claude Code against the running app — the four demo asks plus the HourlyRate check. This is the spec's final acceptance and needs a human watching the answers.

---

## Plan Self-Review Notes

- **Spec coverage:** all 10 tools (Tasks 6–12), 8 entities + seed bias (2–4), security incl. member-deny + service user (4, 8), Serilog CLEF (5), drift + demo script (11), E2E + acceptance (13). Out-of-scope items from the spec are absent by design.
- **Deliberate simplifications (ponytail, marked in code):** `top` and grouping applied in memory after the secured query materializes — correct at seeded scale (≤500 rows), upgrade path `GetObjectsQuery<T>`; Updater uses a dev-constant McpAgent password mirrored in appsettings.
- **Known drift risks for the implementer:** `ModelContextProtocol.AspNetCore` is prerelease — method names (`WithHttpTransport`, `MapMcp`, `McpException`) may shift; the SDK repo README is the tiebreaker. XAF login-page selectors in Task 13 may need adjusting to the rendered DOM.


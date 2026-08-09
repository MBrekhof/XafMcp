# XafMcp — Design Spec

**Date:** 2026-08-09 · **Status:** Approved design, pre-implementation · **Type:** Private POC

> **Supersedes** the earlier XafMCP design (stdio companion app for XafMaui, DX 25.2, shared DB) —
> now in `docs/archive/`. Two of its tools (`list_roles`, `explain_permissions`) were adopted into
> this design; its `implementation-notes.md` remains useful implementation reference.

## Goal

Prove that an XAF-based LOB application can expose itself as an MCP server — metadata, secured data
access, log forensics, and schema-drift checks — so an LLM client (Claude Code) can interrogate the
live app: compose region/product opportunity reports, project status reports, investigate log errors,
and verify DB/model sync. The data-facing tools are **generic and metadata-driven** (built on
ITypesInfo), proving the "generic infrastructure, app-specific features" thesis for later reuse in
WLN apps.

## Decisions (settled in brainstorming)

| Question | Decision |
|---|---|
| Purpose | Private POC; publishable later if it proves out (XafHeadless path) |
| Host shape | Embedded HTTP MCP endpoint in the XAF Blazor Server process |
| Database | SQL Server LocalDB (`(localdb)\MSSQLLocalDB`, db `XafMcp`) |
| Security | XAF security ON; MCP tools run as fixed `McpAgent` service user via SecuredObjectSpace |
| Tool surface | Generic metadata-driven data tools + schema-drift + log forensics; **all read-only** |
| Layout | Two projects: `XafMcp.Module` + `XafMcp.Blazor.Server` |

## Architecture

.NET 10, DevExpress **26.1.4** (pinned in csproj), EF Core, SQL Server LocalDB.

```
┌─────────────────────────────────────────────────────┐
│ XafMcp.Blazor.Server (:5210)                        │
│  ├── stock XAF Blazor Server UI  (Admin user)       │
│  ├── Serilog → console + rolling CLEF JSON files    │
│  └── /mcp  Streamable HTTP (ModelContextProtocol    │
│       .AspNetCore) — MCP tools as DI classes,       │
│       data via SecuredObjectSpace as McpAgent       │
└──────────────────────┬──────────────────────────────┘
                       │ EF Core
              ┌────────▼────────┐        ┌──────────────┐
              │ LocalDB: XafMcp │        │ logs/*.clef  │
              └─────────────────┘        └──────────────┘

Client: Claude Code →  claude mcp add --transport http xafmcp http://localhost:5210/mcp
```

- The app must be running for MCP to work — deliberate: the demo story is an LLM interrogating a
  **live** LOB app (live logs, live data, live security).
- MCP endpoint is anonymous and bound to localhost. Data authorization happens entirely inside XAF
  security, not at the HTTP layer.

### Projects

| Project | Contents |
|---|---|
| `XafMcp.Module` | Entities, `XafMcpDbContext`, `ModuleUpdater` (demo data + security seed) |
| `XafMcp.Blazor.Server` | XAF Blazor host, Serilog config, MCP endpoint + tool classes |
| `XafMcp.Tests` | MSTest 4 unit tests (drift comparer, CLEF parser, query translation) |
| `XafMcp.E2E` | Microsoft.Playwright.NUnit login smoke + HTTP MCP smoke |

NuGet: `ModelContextProtocol.AspNetCore` (prerelease), `Serilog.AspNetCore`,
`Serilog.Formatting.Compact`, `Serilog.Sinks.File`.

## Entities (XafMcp.Module)

All EF Core, authored per the `xaf-efcore-entities` skill rules (virtual properties, BaseObject
pattern, initialized collections, explicit decimal precision, aggregated child collections).

| Entity | Properties | Notes |
|---|---|---|
| `Region` | Name | Lookup |
| `Customer` | Name, Region→Region, City | |
| `Person` | FirstName, LastName, Email, Phone, **HourlyRate** (decimal 19,4) | FullName calculated. HourlyRate is member-denied to McpAgent — the security demo |
| `Product` | Name, Category (enum: Hardware/Software/Services/Consumables), UnitPrice (19,4), Active | |
| `Order` | Customer→Customer, OrderDate, Status (enum: New/Shipped/Completed/Cancelled), Total (19,4, stored, recalculated from lines) | Lines aggregated |
| `OrderLine` | Order (aggregated parent), Product→Product, Quantity, UnitPrice (19,4), Discount (19,4), LineTotal calculated | |
| `Project` | Name, Customer→Customer, Manager→Person, StartDate, DueDate, Status (enum: Planned/Active/OnHold/Completed), Budget (19,4) | Tasks aggregated |
| `ProjectTask` | Subject, Project (aggregated parent), AssignedTo→Person, Status (enum: Open/InProgress/Blocked/Done), DueDate, EstimatedHours, ActualHours | Named `ProjectTask`, never `Task` (System.Threading.Tasks clash) |

### Demo data (ModuleUpdater, deterministic — `Random(42)`)

~5 regions, ~30 customers, ~20 products, ~500 orders over the trailing 24 months, 6 projects,
~60 tasks. **Regional/category bias baked in** (e.g. Software over-indexes in one region, Services
growing quarter-over-quarter in another) so "opportunity by region" reports find real signal, not
uniform noise. Seeding is idempotent (skip when data exists).

## MCP tool surface (10 tools, all read-only)

Registered via `ModelContextProtocol.AspNetCore` attribute-based tool classes; DI gives them
`ITypesInfo`, an object-space factory, and config. Every tool that touches data opens a
**SecuredObjectSpace as McpAgent** — no non-secured access anywhere in the MCP path.

### Data (generic, metadata-driven)

| Tool | Input | Output |
|---|---|---|
| `list_entities` | — | Persistent module entities: name, caption |
| `describe_entity` | `entity` | Properties: name, CLR type, nullable, enum values, reference target entity; key property |
| `query_entities` | `entity`, `criteria?` (XAF criteria string), `sort?`, `top?` (default 50, max 500), `properties?` | JSON rows; scalar values + display text for references |
| `aggregate_entities` | `entity`, `group_by` (property path, e.g. `Customer.Region.Name`), `measure?` + `function` (count/sum/avg/min/max), `criteria?` | Grouped rows |

Criteria strings parse via `CriteriaOperator.Parse`. `describe_entity` output is the LLM's contract
for writing criteria — property names it returns are exactly what `query`/`aggregate` accept.

### Security insight (adopted from the archived design)

| Tool | Input | Output |
|---|---|---|
| `list_roles` | — | Roles: name, administrative flag, user count, per-type permission summary (e.g. `Customer:R`, `Person:R minus HourlyRate`) |
| `explain_permissions` | `role?` (default `McpAgent`), `entity?` | Type-, object-, and member-level permission breakdown from `PermissionPolicyRole` traversal (TypePermissions → ObjectPermissions/MemberPermissions) |

Both read the security model through the secured object space; they answer "what can the MCP agent
(or any role) actually see and why" — including surfacing the `Person.HourlyRate` member-deny
explicitly.

### Schema

| Tool | Behavior |
|---|---|
| `check_schema_drift` | Compares `DbContext.Model.GetRelationalModel()` against `INFORMATION_SCHEMA.TABLES/COLUMNS`. Reports: table in DB not in model, column in DB not in model, model column missing in DB, type mismatch, length mismatch, precision/scale mismatch, nullability mismatch. Structured JSON findings with severity |

A dev-only script `docs/drift-demo.sql` deliberately introduces drift (extra column
`Customer.LegacyCode`, widen an nvarchar, flip a nullability) — all **non-breaking** for EF reads —
so the demo produces findings. README documents apply via `sqlcmd`.

### Logs (Serilog CLEF files)

Serilog writes compact JSON (CLEF) rolling daily files to `logs/xafmcp-YYYYMMDD.clef.json`,
14-day retention, `shared: true`.

| Tool | Input | Output |
|---|---|---|
| `search_logs` | `from?`, `to?`, `min_level?`, `contains?` (text or regex), `top?` (default 100) | Matching events: timestamp, level, message, exception, source |
| `summarize_logs` | `from?`, `to?` | Counts per level, top exception types, top source contexts |

### Info

| Tool | Output |
|---|---|
| `get_server_info` | App version, DX version, .NET version, uptime, DB name, MCP identity (`McpAgent`), row caps |

## Security

- `SecurityStrategyComplex` + `AuthenticationStandard`.
- **Admin** — UI user, full access, created by updater (dev password in config).
- **McpAgent** — service user; password from `appsettings.Development.json` (gitignored); not shown
  on the login screen. Authenticated programmatically per request-scope, same pattern as the
  Hangfire `XafJobScopeInitializer` service-user approach.
- **McpAgent role:** read permission on all module types; **member-deny read** on
  `Person.HourlyRate`; **no write/create/delete permissions on anything**. Read-only is enforced by
  role, and additionally by construction (no tool calls `CommitChanges`).
- Acceptance for the security demo: a query/aggregate touching `Person.HourlyRate` either omits the
  member or returns the protected-content marker — it never returns the value.

## Error handling

- Tool errors return MCP error results with text the LLM can self-correct on: criteria parse failures
  return the parser message; unknown entity/property returns the valid names list.
- Full exceptions logged to Serilog (which feeds the forensics demo); never returned to the client
  beyond a one-line message.
- Guardrails: `top` cap 500 rows, aggregate group cap 500 groups, SQL command timeout 30s.

## Testing

- **Unit (MSTest 4, `XafMcp.Tests`):** drift comparer against faked INFORMATION_SCHEMA rows; CLEF
  parser (well-formed, malformed lines skipped); criteria→query and aggregate path resolution.
  Assert.IsEmpty/HasCount forms per MSTEST0037.
- **E2E (`XafMcp.E2E`, Playwright NUnit per `xaf-playwright-testing`):** XAF login smoke;
  HTTP MCP smoke — initialize session, `list_entities`, one `query_entities`, assert row shape.
- **Acceptance (manual, scripted in README):** with app running and Claude Code connected:
  1. "Report on product opportunities per region" → uses describe/aggregate/query, produces a report
     with the seeded regional bias visible.
  2. "Project status report" → per-project rollup incl. task states and overdue items.
  3. "Any errors in the app last week?" → summarize/search logs.
  4. "Is the database in sync with the model?" → drift findings after `drift-demo.sql` applied.
  5. "What is the MCP agent allowed to do?" → `explain_permissions` names the read-only type grants
     and the `Person.HourlyRate` member-deny.
  6. HourlyRate never appears in any output.

## Out of scope (deliberate)

Write/mutation tools · per-request MCP auth · MCP resources/prompts · PostgreSQL · Docker ·
in-app report rendering (the LLM client composes reports) · extraction into a shared library
(happens after the POC proves the tool shapes).

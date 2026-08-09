# CLAUDE.md

## Project Overview

**XafMcp** — private POC proving that a DevExpress XAF LOB application can expose itself as an MCP
server: metadata, secured data access, log forensics, and schema-drift checks, consumed by Claude
Code as the MCP client.

## Architecture

Standard XAF Blazor Server app (own entities, LocalDB) with an **embedded Streamable-HTTP MCP
endpoint** (`ModelContextProtocol.AspNetCore`) at `http://localhost:5210/mcp` in the same process.
MCP tools are DI classes; all data access runs through a **SecuredObjectSpace as the `McpAgent`
service user** (read-only role, member-deny on `Person.HourlyRate`). Serilog writes CLEF JSON
rolling files that the log-forensics tools read.

- .NET 10 · DevExpress **26.1.4** (pinned) · EF Core · SQL Server LocalDB (db `XafMcp`)
- Projects: `XafMcp.Module` (entities, DbContext, updater/seed) · `XafMcp.Blazor.Server` (UI + MCP)
  · `XafMcp.Tests` (MSTest 4) · `XafMcp.E2E` (Playwright NUnit + MCP HTTP smoke)
- 10 read-only tools: `list_entities`, `describe_entity`, `query_entities`, `aggregate_entities`,
  `list_roles`, `explain_permissions`, `check_schema_drift`, `search_logs`, `summarize_logs`,
  `get_server_info`

## Authoritative documents

- `docs/superpowers/specs/2026-08-09-xafmcp-design.md` — the approved design spec
- `docs/archive/` — the superseded 2026 design (stdio companion for XafMaui, DX 25.2). Its
  `implementation-notes.md` snippets (IMemberInfo DTO projection, PermissionPolicyRole traversal)
  remain useful reference; its architecture does not apply.

## Conventions

- Entity authoring follows the `xaf-efcore-entities` skill (virtual properties, ObservableCollection
  + [Aggregated], explicit FKs, decimal precision in OnModelCreating).
- The task-shaped entity is named `ProjectTask`, never `Task`.
- MCP path is read-only by construction: no tool calls `CommitChanges`.
- Naming: **XafMcp** (folder on disk is historically `XafMCP`; namespaces/projects use `XafMcp`).

## Run

```
dotnet run --project XafMcp.Blazor.Server   # UI + /mcp on :5210
claude mcp add --transport http xafmcp http://localhost:5210/mcp
```

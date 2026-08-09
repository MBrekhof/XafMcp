# XafMcp — an XAF LOB app as an MCP server

Private POC: a DevExpress XAF Blazor Server application (Customers/Orders/Products/Projects/Tasks
on LocalDB) that embeds a Streamable-HTTP MCP endpoint in the same process, so an LLM client
(Claude Code) can interrogate the **living application**:

- **Data & metadata** — generic, ITypesInfo-driven tools (`list_entities`, `describe_entity`,
  `query_entities` with XAF criteria strings, `aggregate_entities`) compose region/product
  opportunity reports and project status reports without any report code in the app.
- **Security insight** — tools run as a read-only `McpAgent` service user through XAF's
  SecuredObjectSpace; `list_roles` / `explain_permissions` show why (e.g. `Person.HourlyRate` is
  member-denied and never reaches the LLM).
- **Log forensics** — Serilog CLEF files queried via `search_logs` / `summarize_logs`.
- **Schema drift** — `check_schema_drift` compares the EF Core relational model against
  `INFORMATION_SCHEMA` (extra/missing columns, type/precision/nullability mismatches).

**Status:** design approved, implementation pending. Design: `docs/superpowers/specs/2026-08-09-xafmcp-design.md`.
Superseded earlier design (stdio companion app for XafMaui, DX 25.2): `docs/archive/`.

**Stack:** .NET 10 · DevExpress XAF 26.1.4 (EF Core) · SQL Server LocalDB · `ModelContextProtocol.AspNetCore` · Serilog.

DevExpress license required. Private project.

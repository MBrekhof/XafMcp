# Acceptance run — 2026-08-10

Executed by Claude Code over the live MCP endpoint (`http://localhost:5210/mcp`), app running,
DevExpress 26.1.4, .NET 10.0.10, database `XafMcp`, identity `McpAgent`. All six README steps
executed against real tool output; no step simulated.

**Verdict: PASS** (6/6 steps; two minor observations, no blockers).

## 1. Product opportunities per region — PASS

Used `describe_entity`, `aggregate_entities`, `query_entities`. Orders span Aug 2024 – Aug 2026.

Revenue (sum of Order.Total): West 1,352,003 · South 1,254,811 · Central 1,085,660 · East 974,896 · North 947,429.

Units by category: Hardware 4,866 · Software 4,684 · Services 2,892 · Consumables 2,292.

Seeded biases showed up as required:

- **Software → North:** North sold 1,701 software units — 36% of all software, double the #2
  region (South, 901) — while buying almost no Hardware (303 units vs West's 1,371).
- **Services growing in South:** South is the largest Services region (741 units) and grew
  331 → 410 units year-over-year (**+24%**). (Central grew faster in % terms, +75%, but from a
  small base: 167 → 292.)

Opportunities: cross-sell Hardware/Services into North's software base; expand Services capacity
in South (and watch Central); East is the laggard — lowest revenue, Services declining −14%.

## 2. Project status report incl. overdue — PASS

6 projects: 3 Active (A, D, F), 1 OnHold (C), 2 Completed (B, E). 60 tasks:
20 Open, 7 InProgress, 18 Blocked, 15 Done.

**17 overdue tasks** (`Not (Status = 'Done') And DueDate < #2026-08-10#`): A 4 · B 1 · C 3 ·
D 3 · E 5 · F 1. Oldest: "Deploy migration" (Project A, Blocked, due 2026-06-14).

Data quirks in the seed (reported, as a real agent should): Projects A and D are Active but past
their project due dates; Project E is marked Completed yet carries 5 overdue non-done tasks.

## 3. Errors/warnings in app logs — PASS

`summarize_logs`: 262 events since 2026-08-09 13:29, 7 errors, none from today (at check time).
`search_logs` showed all 7 were accounted for: 5 deliberate negative tests / dev probes, 1 handled
empty-username login, 1 stale `check_schema_drift` failure (ModelDifference EF validation) that no
longer reproduces — the tool works (see step 4).

## 4. Schema drift — PASS

Pre-check found the DB had only partial drift (LegacyImport table); Customers.LegacyCode and the
Regions.Name alteration were missing — the DB was evidently reset after the demo was last applied.
Re-applied `docs/drift-demo.sql` per the README, then `check_schema_drift` reported **all 4
expected findings**:

| Kind | Table.Column | Model | DB |
|---|---|---|---|
| table_only_in_db | LegacyImport | — | exists |
| column_only_in_db | Customers.LegacyCode | — | nvarchar(20) |
| type_mismatch | Regions.Name | nvarchar(100) | nvarchar(200) |
| nullability_mismatch | Regions.Name | NOT NULL | NULL |

## 5. What may the MCP agent see, and why — PASS

`list_roles` + `explain_permissions`: role **McpReadOnly** (1 user), read-only on the 8 business
entities plus the 4 security-policy types, unlisted types denied by default. Member-level denies:
**Person.HourlyRate** (read deny) and **PermissionPolicyUser.StoredPassword** (read deny). No
write/create/delete grants anywhere — read-only by permission as well as by construction.

## 6. No HourlyRate value anywhere — PASS

- Full `query_entities('Person')`: rows contain FirstName/LastName/Email/Phone/ID only.
- `query_entities('Person', properties: ['HourlyRate'])`: returns empty objects — silently omitted.
- `aggregate_entities` over HourlyRate: hard deny ("Access to 'Person.HourlyRate' is denied for
  the MCP role" — seen in yesterday's logs at 18:58).
- No HourlyRate value appears in any output of this run.

## Observations (non-blocking)

1. **`<>` against an enum crashes criteria conversion.** `Status <> 'Done'` parses, but
   DevExpress's `CriteriaToExpressionConverter` throws `InvalidOperationException` ("binary
   operator NotEqual is not defined for ProjectTaskStatus and String"), which surfaces as a
   generic MCP error instead of the friendly hint other invalid criteria get (`DataTools.cs`
   guards parse errors but not conversion errors). Workaround: `Not (Status = 'Done')`.
   Candidate one-line fix: catch `InvalidOperationException` around query materialization and
   rethrow as `McpException` with the enum hint.
2. **Client-side quirk, not server:** on one attempt the MCP client transport HTML-encoded
   `<`/`>=` in criteria (`&lt;`, `&gt;=`); the server's parser error message correctly pinpointed
   it and a clean retry succeeded — arguably the error-message design proving its worth.

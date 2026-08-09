# XafMCP Tool Specifications

## 1. xaf_list_entities

List all registered XAF business objects.

**Parameters:** none

**Returns:**
```json
{
  "entities": [
    {
      "typeName": "XafMaui.Module.BusinessObjects.Client",
      "shortName": "Client",
      "defaultProperty": "Name",
      "isNavigable": true,
      "navigationGroup": "Clients",
      "propertyCount": 12,
      "hasAuditTrail": true
    }
  ]
}
```

**Implementation:** `XafTypesInfo.Instance.PersistentTypes` → filter to registered entities, cross-reference with Application Model navigation items.

---

## 2. xaf_describe_entity

Full metadata for a single entity type.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `entityName` | string | yes | Short name ("Client") or full name ("XafMaui.Module.BusinessObjects.Client") |
| `includePermissions` | bool | no | Include per-role permission breakdown (default: false) |

**Returns:**
```json
{
  "typeName": "XafMaui.Module.BusinessObjects.Client",
  "shortName": "Client",
  "baseType": "BaseObjectInt",
  "defaultProperty": "Name",
  "keyProperty": "ID",
  "keyType": "int",
  "attributes": ["DefaultClassOptions", "DefaultProperty(Name)"],
  "properties": [
    {
      "name": "Name",
      "type": "string",
      "isRequired": true,
      "isVirtual": true,
      "maxLength": null,
      "defaultValue": "",
      "attributes": []
    },
    {
      "name": "ContactPersons",
      "type": "IList<ContactPerson>",
      "isCollection": true,
      "isAggregated": true,
      "inverseProperty": "Client"
    },
    {
      "name": "Projects",
      "type": "IList<Project>",
      "isCollection": true,
      "isAggregated": false,
      "inverseProperty": "Client"
    }
  ],
  "validationRules": [
    { "property": "Name", "rule": "Required", "message": "Name is required" }
  ],
  "permissions": {
    "Administrators": { "read": true, "write": true, "create": true, "delete": true, "navigate": true },
    "Consultants": { "read": true, "write": false, "create": false, "delete": false, "navigate": true },
    "BackOffice": { "read": true, "write": true, "create": true, "delete": false, "navigate": true }
  }
}
```

**Implementation:**
```csharp
var typeInfo = XafTypesInfo.Instance.FindTypeInfo(entityName);
// Properties from typeInfo.Members
// Validation from typeInfo.FindAttributes<RuleBaseAttribute>()
// Permissions from SecurityStrategy.GetPermissions() per role
```

---

## 3. xaf_query

Query business objects through a secured ObjectSpace.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `entityName` | string | yes | Entity type to query |
| `criteria` | string | no | XPO criteria string, e.g. `"Name LIKE '%Acme%'"` |
| `properties` | string[] | no | Properties to include (default: all visible) |
| `sortBy` | string | no | Sort property |
| `sortDesc` | bool | no | Descending sort (default: false) |
| `top` | int | no | Max rows (default: 20, max: 100) |
| `skip` | int | no | Rows to skip for paging |
| `asRole` | string | no | Execute as this role (default: Administrators) |

**Returns:**
```json
{
  "entityName": "Client",
  "totalCount": 42,
  "returned": 20,
  "objects": [
    { "ID": 1, "Name": "Acme Corp", "City": "Amsterdam", "ProjectCount": 3 },
    { "ID": 2, "Name": "Contoso Ltd", "City": "Rotterdam", "ProjectCount": 1 }
  ],
  "executedAs": "Administrators",
  "securityFiltered": false
}
```

**Implementation:**
```csharp
// Create ObjectSpace as the specified role
using var os = objectSpaceFactory.CreateObjectSpace(entityType);
var criteria = CriteriaOperator.Parse(criteriaString);
var objects = os.GetObjects(entityType, criteria, new SortProperty(sortBy, sortDesc ? SortingDirection.Descending : SortingDirection.Ascending));
// Serialize selected properties, note if security filtered any results
```

**Security note:** Always uses a secured ObjectSpace. The `asRole` parameter impersonates a test user with that role. Objects the role can't see are silently excluded — the response indicates `securityFiltered: true` when count differs from unfiltered.

---

## 4. xaf_explain_permissions

Explain what a specific role can do with a specific entity.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `roleName` | string | yes | Role name |
| `entityName` | string | no | Entity type (omit for full role dump) |

**Returns:**
```json
{
  "roleName": "Consultants",
  "isAdministrative": false,
  "entityPermissions": {
    "TimeEntry": {
      "typeLevel": {
        "read": "allow",
        "write": "allow",
        "create": "allow",
        "delete": "deny"
      },
      "objectLevel": [
        {
          "operations": "ReadWriteAccess",
          "criteria": "User.ID = CurrentUserId()",
          "state": "allow",
          "description": "Consultants can only read/write their own time entries"
        }
      ],
      "memberLevel": []
    },
    "Client": {
      "typeLevel": {
        "read": "allow",
        "write": "deny",
        "create": "deny",
        "delete": "deny"
      },
      "objectLevel": [],
      "memberLevel": []
    }
  },
  "navigationAccess": ["TimeEntry", "Client", "Project"]
}
```

**Implementation:**
```csharp
var role = os.FindObject<PermissionPolicyRole>(CriteriaOperator.Parse("Name = ?", roleName));
foreach (var tp in role.TypePermissions)
{
    // tp.TargetType, tp.AllowRead, tp.AllowWrite, etc.
    // tp.ObjectPermissions → criteria-based rules
    // tp.MemberPermissions → property-level rules
}
```

---

## 5. xaf_list_navigation

Full navigation tree as the Application Model defines it.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `asRole` | string | no | Filter to items visible to this role |

**Returns:**
```json
{
  "groups": [
    {
      "id": "Clients",
      "caption": "Clients",
      "items": [
        { "id": "Client_ListView", "caption": "Clients", "entityName": "Client", "viewType": "ListView" },
        { "id": "ContactPerson_ListView", "caption": "Contact Persons", "entityName": "ContactPerson", "viewType": "ListView" }
      ]
    },
    {
      "id": "Projects",
      "caption": "Projects",
      "items": [
        { "id": "Project_ListView", "caption": "Projects", "entityName": "Project" },
        { "id": "ProjectTask_ListView", "caption": "Tasks", "entityName": "ProjectTask" }
      ]
    }
  ]
}
```

**Implementation:** `Application.Model.NavigationItems` tree traversal, cross-referenced with security for role filtering.

---

## 6. xaf_list_roles

All roles with permission summary.

**Parameters:** none

**Returns:**
```json
{
  "roles": [
    {
      "name": "Administrators",
      "isAdministrative": true,
      "userCount": 1,
      "permissionCount": 0,
      "description": "Bypasses all security checks"
    },
    {
      "name": "Consultants",
      "isAdministrative": false,
      "userCount": 3,
      "permissionCount": 12,
      "typePermissions": ["TimeEntry:RWCD", "Client:R", "Project:R", "ProjectTask:R"],
      "hasObjectLevelRules": true
    }
  ]
}
```

---

## 7. xaf_run_report

Execute a predefined XAF report.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `reportName` | string | yes | Display name of the predefined report |
| `parameters` | object | no | Report parameters as key-value pairs |
| `format` | string | no | Output format: "pdf" (default), "csv", "xlsx" |

**Returns:** Base64-encoded file content with MIME type, or error if report not found.

**Implementation:**
```csharp
var reportData = os.FindObject<ReportDataV2>(CriteriaOperator.Parse("DisplayName = ?", reportName));
var report = ReportDataProvider.ReportsStorage.LoadReport(reportData);
// Apply parameters from the request
// Export to requested format
using var ms = new MemoryStream();
report.ExportToPdf(ms); // or ExportToCsv, ExportToXlsx
return Convert.ToBase64String(ms.ToArray());
```

---

## 8. xaf_model_diff

Compare Application Model with database schema.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `entityName` | string | no | Specific entity (omit for full comparison) |

**Returns:**
```json
{
  "status": "mismatch",
  "differences": [
    {
      "entityName": "Client",
      "type": "missing_column",
      "property": "TaxNumber",
      "detail": "Property exists in model but not in database"
    },
    {
      "entityName": "Project",
      "type": "type_mismatch",
      "property": "Budget",
      "modelType": "decimal(18,2)",
      "databaseType": "decimal(18,0)",
      "detail": "Precision mismatch — possible silent truncation"
    }
  ],
  "pendingMigrations": ["20260310_AddTaxNumber"]
}
```

**Implementation:** Compare `XafTypesInfo` model with EF Core's `context.Database.GetPendingMigrations()` and `context.Model` vs `context.Database.GetAppliedMigrations()`.

---

## 9. xaf_audit_query

Query change history from XAF's audit trail.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `entityName` | string | yes | Entity type |
| `objectId` | string | no | Specific object ID (omit for all changes to this type) |
| `property` | string | no | Filter to changes on a specific property |
| `since` | string | no | ISO date, changes after this date |
| `top` | int | no | Max entries (default: 50) |

**Returns:**
```json
{
  "entries": [
    {
      "timestamp": "2026-03-10T14:30:00Z",
      "user": "Admin",
      "entityName": "TimeEntry",
      "objectId": "42",
      "operation": "Modified",
      "changes": [
        { "property": "Hours", "oldValue": "4.0", "newValue": "6.5" },
        { "property": "Description", "oldValue": "Code review", "newValue": "Code review + testing" }
      ]
    }
  ]
}
```

**Implementation:** Query `AuditDataItemPersistent` objects (or EF Core equivalent) from XAF's audit trail system.

---

## 10. xaf_validate

Run XAF validation rules against an object or hypothetical data.

**Parameters:**
| Param | Type | Required | Description |
|---|---|---|---|
| `entityName` | string | yes | Entity type |
| `data` | object | yes | Property values to validate |
| `context` | string | no | Validation context: "Save" (default), "Delete", "Custom" |

**Returns:**
```json
{
  "isValid": false,
  "errors": [
    { "property": "Hours", "rule": "RuleRange", "message": "Hours must be between 0.25 and 24" },
    { "property": "ProjectTask", "rule": "RuleRequiredField", "message": "Project Task is required" }
  ]
}
```

**Implementation:**
```csharp
var obj = os.CreateObject(entityType);
// Set properties from data
var result = Validator.RuleSet.ValidateTarget(obj, DefaultContexts.Save);
// Return broken rules
```

---

## Implementation Priority

1. **Phase 1 (core metadata):** `xaf_list_entities`, `xaf_describe_entity`, `xaf_list_roles` — read-only, no security impersonation needed
2. **Phase 2 (security insight):** `xaf_explain_permissions`, `xaf_list_navigation` — the killer features for debugging
3. **Phase 3 (data access):** `xaf_query`, `xaf_validate` — secured ObjectSpace queries
4. **Phase 4 (advanced):** `xaf_run_report`, `xaf_audit_query`, `xaf_model_diff` — require additional XAF modules

## Hosting Options

### Option A: Embedded in XAF Blazor app (simplest)
Add MCP endpoints alongside existing Blazor Server. Shares the same `XafApplication` instance. Limitation: MCP stdio transport requires a separate process.

### Option B: Standalone headless XAF app (recommended)
Separate console app that boots XAF without Blazor UI. Supports stdio transport for Claude Code. Points to the same database. Can run as a background service.

### Option C: Sidecar with SSE transport
Runs as a separate HTTP service alongside the Blazor app. Uses SSE (Server-Sent Events) transport. Works with any MCP client, not just Claude Code.

**Recommendation:** Start with **Option B** — a standalone console app with stdio transport. This is the simplest integration with Claude Code (`claude mcp add`), and a headless XAF app is lightweight to configure.

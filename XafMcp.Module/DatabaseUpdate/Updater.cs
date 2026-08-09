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
            // Security-metadata read: the member-deny projection and Task 10's security-insight tools
            // introspect the permission model THROUGH the secured space; without these grants the
            // role lookup silently returns empty (found in Task 8).
            mcpRole.SetTypePermission<PermissionPolicyRole>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<PermissionPolicyTypePermissionObject>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<PermissionPolicyMemberPermissionsObject>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.SetTypePermission<PermissionPolicyUser>(SecurityOperations.Read, SecurityPermissionState.Allow);
            mcpRole.AddMemberPermission<PermissionPolicyUser>(SecurityOperations.Read, "StoredPassword", null, SecurityPermissionState.Deny); // defense in depth
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

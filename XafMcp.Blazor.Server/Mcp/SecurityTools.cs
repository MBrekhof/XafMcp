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

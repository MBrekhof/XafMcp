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

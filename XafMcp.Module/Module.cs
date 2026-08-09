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

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

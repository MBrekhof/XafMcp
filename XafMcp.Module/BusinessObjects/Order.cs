using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(DisplayName))]
public class Order : BaseObjectInt {
    public virtual int? CustomerId { get; set; }
    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    public virtual DateTime OrderDate { get; set; }
    public virtual OrderStatus Status { get; set; }

    // Stored so XAF criteria strings can filter on it server-side ("Total > 1000")
    public virtual decimal Total { get; set; }

    [Aggregated]
    public virtual IList<OrderLine> Lines { get; set; } = new ObservableCollection<OrderLine>();

    [NotMapped]
    public string DisplayName => $"Order {ID} ({OrderDate:yyyy-MM-dd})";

    public override void OnSaving() {
        base.OnSaving();
        Total = Lines.Sum(l => l.LineTotal);
    }
}

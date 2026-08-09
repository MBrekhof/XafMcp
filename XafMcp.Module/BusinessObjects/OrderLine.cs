using System.ComponentModel.DataAnnotations.Schema;

namespace XafMcp.Module.BusinessObjects;

public class OrderLine : BaseObjectInt {
    public virtual int? OrderId { get; set; }
    [ForeignKey(nameof(OrderId))]
    public virtual Order? Order { get; set; }

    public virtual int? ProductId { get; set; }
    [ForeignKey(nameof(ProductId))]
    public virtual Product? Product { get; set; }

    public virtual int Quantity { get; set; }
    public virtual decimal UnitPrice { get; set; }
    public virtual decimal Discount { get; set; } // fraction 0..1

    [NotMapped]
    public decimal LineTotal => Quantity * UnitPrice * (1m - Discount);
}

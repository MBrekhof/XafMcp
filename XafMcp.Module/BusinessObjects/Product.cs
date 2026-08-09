using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Product : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;

    public virtual ProductCategory Category { get; set; }
    public virtual decimal UnitPrice { get; set; }
    public virtual bool Active { get; set; } = true;
}

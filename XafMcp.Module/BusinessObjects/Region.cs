using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Region : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;
}

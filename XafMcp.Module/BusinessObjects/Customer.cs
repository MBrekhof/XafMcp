using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Customer : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;

    public virtual int? RegionId { get; set; }
    [ForeignKey(nameof(RegionId))]
    public virtual Region? Region { get; set; }

    [MaxLength(100)]
    public virtual string City { get; set; } = string.Empty;

    public virtual IList<Order> Orders { get; set; } = new ObservableCollection<Order>();
    public virtual IList<Project> Projects { get; set; } = new ObservableCollection<Project>();
}

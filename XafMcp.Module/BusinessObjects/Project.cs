using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Name))]
public class Project : BaseObjectInt {
    [Required]
    [MaxLength(100)]
    public virtual string Name { get; set; } = string.Empty;

    public virtual int? CustomerId { get; set; }
    [ForeignKey(nameof(CustomerId))]
    public virtual Customer? Customer { get; set; }

    public virtual int? ManagerId { get; set; }
    [ForeignKey(nameof(ManagerId))]
    public virtual Person? Manager { get; set; }

    public virtual DateTime StartDate { get; set; }
    public virtual DateTime? DueDate { get; set; }
    public virtual ProjectStatus Status { get; set; }
    public virtual decimal Budget { get; set; }

    [Aggregated]
    public virtual IList<ProjectTask> Tasks { get; set; } = new ObservableCollection<ProjectTask>();
}

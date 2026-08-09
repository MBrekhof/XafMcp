using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(Subject))]
public class ProjectTask : BaseObjectInt {
    [Required]
    [MaxLength(200)]
    public virtual string Subject { get; set; } = string.Empty;

    public virtual int? ProjectId { get; set; }
    [ForeignKey(nameof(ProjectId))]
    public virtual Project? Project { get; set; }

    public virtual int? AssignedToId { get; set; }
    [ForeignKey(nameof(AssignedToId))]
    public virtual Person? AssignedTo { get; set; }

    public virtual ProjectTaskStatus Status { get; set; }
    public virtual DateTime? DueDate { get; set; }
    public virtual decimal EstimatedHours { get; set; }
    public virtual decimal ActualHours { get; set; }
}

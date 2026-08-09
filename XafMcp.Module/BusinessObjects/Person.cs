using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using DevExpress.Persistent.Base;

namespace XafMcp.Module.BusinessObjects;

[DefaultClassOptions]
[DefaultProperty(nameof(FullName))]
public class Person : BaseObjectInt {
    [Required]
    [MaxLength(60)]
    public virtual string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(60)]
    public virtual string LastName { get; set; } = string.Empty;

    [MaxLength(200)]
    public virtual string Email { get; set; } = string.Empty;

    [MaxLength(40)]
    public virtual string Phone { get; set; } = string.Empty;

    // Member-denied to the MCP role — the security demo (spec §4)
    public virtual decimal HourlyRate { get; set; }

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();
}

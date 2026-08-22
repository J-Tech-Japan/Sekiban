using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sekiban.Dcb.Postgres.DbModels;

/// <summary>Append-only reconciliation evidence for a head bypass observed from authoritative tag rows.</summary>
[Table("dcb_tag_head_violations")]
public sealed class DbTagHeadViolation
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string ServiceId { get; set; } = string.Empty;

    [Required]
    public string Tag { get; set; } = string.Empty;

    /// <summary>Whether the observed prior head was the explicit proven-empty representation.</summary>
    public bool PreviousHeadWasEmpty { get; set; }

    /// <summary>Prior non-empty head, or an empty string when <see cref="PreviousHeadWasEmpty" /> is true.</summary>
    [Required]
    [MaxLength(100)]
    public string PreviousHeadPosition { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ObservedPosition { get; set; } = string.Empty;

    public DateTime DetectedAtUtc { get; set; }

    [Required]
    [MaxLength(128)]
    public string DetectingWriter { get; set; } = string.Empty;
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sekiban.Dcb.Postgres.DbModels;

/// <summary>
///     Durable, service-scoped head for every PostgreSQL tag that has participated in the canonical tag-head protocol.
///     A row with <see cref="HeadPosition" /> null is an explicit, transactionally proven-empty head; absence of a row is
///     never treated as empty until the bootstrap lookup has created that row.
/// </summary>
[Table("dcb_tag_heads")]
public sealed class DbTagHead
{
    [Required]
    [MaxLength(64)]
    public string ServiceId { get; set; } = string.Empty;

    [Required]
    public string Tag { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? HeadPosition { get; set; }
}

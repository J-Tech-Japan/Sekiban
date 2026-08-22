using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Sekiban.Dcb.Postgres.DbModels;

/// <summary>
///     Provisioning-plane marker set only after every pre-protocol PostgreSQL writer has been drained. It is deliberately
///     per service because the durable head key and the cutover guarantee are per service.
/// </summary>
[Table("dcb_tag_head_enablement_epochs")]
public sealed class DbTagHeadEnablementEpoch
{
    [Key]
    [MaxLength(64)]
    public string ServiceId { get; set; } = string.Empty;

    public DateTime EnabledAtUtc { get; set; }
}

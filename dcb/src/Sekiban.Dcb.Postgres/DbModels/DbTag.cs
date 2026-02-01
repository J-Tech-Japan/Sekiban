using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Sekiban.Dcb.Postgres.DbModels;

/// <summary>
///     Represents a tag reference to an event in the database
///     This table only tracks which tags are associated with which events via SortableUniqueId
/// </summary>
[Table("dcb_tags")]
public class DbTag
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string ServiceId { get; set; } = string.Empty;

    [Required]
    public string Tag { get; set; } = string.Empty;

    [Required]
    public string TagGroup { get; set; } = string.Empty;

    [Required]
    public string EventType { get; set; } = string.Empty;

    [Required]
    public string SortableUniqueId { get; set; } = string.Empty;

    public Guid EventId { get; set; }

    public DateTime CreatedAt { get; set; }

    public static DbTag FromEventTag(
        string tag,
        string tagGroup,
        string sortableUniqueId,
        Guid eventId,
        string eventType,
        string serviceId) =>
        new()
        {
            ServiceId = serviceId,
            Tag = tag,
            TagGroup = tagGroup,
            EventType = eventType,
            SortableUniqueId = sortableUniqueId,
            EventId = eventId,
            CreatedAt = DateTime.UtcNow
        };
}

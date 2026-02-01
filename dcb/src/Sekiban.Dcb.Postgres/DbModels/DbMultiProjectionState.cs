using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Postgres.DbModels;

[Table("dcb_multi_projection_states")]
public class DbMultiProjectionState
{
    [Required]
    [MaxLength(64)]
    public string ServiceId { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string ProjectorName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string ProjectorVersion { get; set; } = string.Empty;

    [Required]
    [MaxLength(512)]
    public string PayloadType { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastSortableUniqueId { get; set; } = string.Empty;

    public long EventsProcessed { get; set; }

    // Gzip compressed state data (null when offloaded)
    public byte[]? StateData { get; set; }

    public bool IsOffloaded { get; set; }

    [MaxLength(512)]
    public string? OffloadKey { get; set; }

    [MaxLength(128)]
    public string? OffloadProvider { get; set; }

    public long OriginalSizeBytes { get; set; }

    public long CompressedSizeBytes { get; set; }

    [Required]
    [MaxLength(100)]
    public string SafeWindowThreshold { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    [Required]
    [MaxLength(32)]
    public string BuildSource { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? BuildHost { get; set; }

    public static DbMultiProjectionState FromRecord(MultiProjectionStateRecord record, string serviceId) =>
        new()
        {
            ServiceId = serviceId,
            ProjectorName = record.ProjectorName,
            ProjectorVersion = record.ProjectorVersion,
            PayloadType = record.PayloadType,
            LastSortableUniqueId = record.LastSortableUniqueId,
            EventsProcessed = record.EventsProcessed,
            StateData = record.StateData,
            IsOffloaded = record.IsOffloaded,
            OffloadKey = record.OffloadKey,
            OffloadProvider = record.OffloadProvider,
            OriginalSizeBytes = record.OriginalSizeBytes,
            CompressedSizeBytes = record.CompressedSizeBytes,
            SafeWindowThreshold = record.SafeWindowThreshold,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            BuildSource = record.BuildSource,
            BuildHost = record.BuildHost
        };

    public MultiProjectionStateRecord ToRecord(byte[]? overrideStateData = null) =>
        new(
            ProjectorName: ProjectorName,
            ProjectorVersion: ProjectorVersion,
            PayloadType: PayloadType,
            LastSortableUniqueId: LastSortableUniqueId,
            EventsProcessed: EventsProcessed,
            StateData: overrideStateData ?? StateData,
            IsOffloaded: IsOffloaded,
            OffloadKey: OffloadKey,
            OffloadProvider: OffloadProvider,
            OriginalSizeBytes: OriginalSizeBytes,
            CompressedSizeBytes: CompressedSizeBytes,
            SafeWindowThreshold: SafeWindowThreshold,
            CreatedAt: CreatedAt,
            UpdatedAt: UpdatedAt,
            BuildSource: BuildSource,
            BuildHost: BuildHost);
}

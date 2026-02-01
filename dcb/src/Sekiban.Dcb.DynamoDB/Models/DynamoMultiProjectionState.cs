using Amazon.DynamoDBv2.Model;
using System.Globalization;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.DynamoDB.Models;

/// <summary>
///     DynamoDB model for multi-projection state storage.
/// </summary>
public class DynamoMultiProjectionState
{
    /// <summary>
    ///     Partition key: SERVICE#{serviceId}#PROJECTOR#{projectorName}
    /// </summary>
    public string Pk { get; set; } = string.Empty;

    /// <summary>
    ///     Sort key: VERSION#{projectorVersion}
    /// </summary>
    public string Sk { get; set; } = string.Empty;

    /// <summary>
    ///     Service ID for tenant isolation.
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    ///     Projector name.
    /// </summary>
    public string ProjectorName { get; set; } = string.Empty;

    /// <summary>
    ///     Projector version.
    /// </summary>
    public string ProjectorVersion { get; set; } = string.Empty;

    /// <summary>
    ///     Payload type.
    /// </summary>
    public string PayloadType { get; set; } = string.Empty;

    /// <summary>
    ///     Last processed sortable unique ID.
    /// </summary>
    public string LastSortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Number of events processed.
    /// </summary>
    public long EventsProcessed { get; set; }

    /// <summary>
    ///     Base64 + Gzip compressed state data (if not offloaded).
    /// </summary>
    public string? StateData { get; set; }

    /// <summary>
    ///     Whether the state is offloaded to blob storage.
    /// </summary>
    public bool IsOffloaded { get; set; }

    /// <summary>
    ///     Blob storage key (if offloaded).
    /// </summary>
    public string? OffloadKey { get; set; }

    /// <summary>
    ///     Blob storage provider name (if offloaded).
    /// </summary>
    public string? OffloadProvider { get; set; }

    /// <summary>
    ///     Original state size in bytes.
    /// </summary>
    public long OriginalSizeBytes { get; set; }

    /// <summary>
    ///     Compressed state size in bytes.
    /// </summary>
    public long CompressedSizeBytes { get; set; }

    /// <summary>
    ///     Safe window threshold for replay.
    /// </summary>
    public string SafeWindowThreshold { get; set; } = string.Empty;

    /// <summary>
    ///     Last updated timestamp.
    /// </summary>
    public string UpdatedAt { get; set; } = string.Empty;

    /// <summary>
    ///     Created timestamp.
    /// </summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    ///     Build source.
    /// </summary>
    public string BuildSource { get; set; } = string.Empty;

    /// <summary>
    ///     Build host.
    /// </summary>
    public string? BuildHost { get; set; }

    /// <summary>
    ///     Converts to DynamoDB attribute values.
    /// </summary>
    public Dictionary<string, AttributeValue> ToAttributeValues()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = Pk },
            ["sk"] = new AttributeValue { S = Sk },
            ["serviceId"] = new AttributeValue { S = ServiceId },
            ["projectorName"] = new AttributeValue { S = ProjectorName },
            ["projectorVersion"] = new AttributeValue { S = ProjectorVersion },
            ["payloadType"] = new AttributeValue { S = PayloadType },
            ["lastSortableUniqueId"] = new AttributeValue { S = LastSortableUniqueId },
            ["eventsProcessed"] = new AttributeValue { N = EventsProcessed.ToString(CultureInfo.InvariantCulture) },
            ["isOffloaded"] = new AttributeValue { BOOL = IsOffloaded },
            ["originalSizeBytes"] = new AttributeValue { N = OriginalSizeBytes.ToString(CultureInfo.InvariantCulture) },
            ["compressedSizeBytes"] = new AttributeValue { N = CompressedSizeBytes.ToString(CultureInfo.InvariantCulture) },
            ["safeWindowThreshold"] = new AttributeValue { S = SafeWindowThreshold },
            ["updatedAt"] = new AttributeValue { S = UpdatedAt },
            ["createdAt"] = new AttributeValue { S = CreatedAt },
            ["buildSource"] = new AttributeValue { S = BuildSource }
        };

        if (!string.IsNullOrEmpty(StateData))
            item["stateData"] = new AttributeValue { S = StateData };
        if (!string.IsNullOrEmpty(OffloadKey))
            item["offloadKey"] = new AttributeValue { S = OffloadKey };
        if (!string.IsNullOrEmpty(OffloadProvider))
            item["offloadProvider"] = new AttributeValue { S = OffloadProvider };
        if (!string.IsNullOrEmpty(BuildHost))
            item["buildHost"] = new AttributeValue { S = BuildHost };

        return item;
    }

    /// <summary>
    ///     Creates from DynamoDB attribute values.
    /// </summary>
    public static DynamoMultiProjectionState FromAttributeValues(Dictionary<string, AttributeValue> item)
    {
        return new DynamoMultiProjectionState
        {
            Pk = item.GetValueOrDefault("pk")?.S ?? string.Empty,
            Sk = item.GetValueOrDefault("sk")?.S ?? string.Empty,
            ServiceId = item.GetValueOrDefault("serviceId")?.S ?? string.Empty,
            ProjectorName = item.GetValueOrDefault("projectorName")?.S ?? string.Empty,
            ProjectorVersion = item.GetValueOrDefault("projectorVersion")?.S ?? string.Empty,
            PayloadType = item.GetValueOrDefault("payloadType")?.S ?? string.Empty,
            LastSortableUniqueId = item.GetValueOrDefault("lastSortableUniqueId")?.S ?? string.Empty,
            EventsProcessed = long.TryParse(item.GetValueOrDefault("eventsProcessed")?.N, out var ep) ? ep : 0,
            StateData = item.GetValueOrDefault("stateData")?.S,
            IsOffloaded = item.GetValueOrDefault("isOffloaded")?.BOOL ?? false,
            OffloadKey = item.GetValueOrDefault("offloadKey")?.S,
            OffloadProvider = item.GetValueOrDefault("offloadProvider")?.S,
            OriginalSizeBytes = long.TryParse(item.GetValueOrDefault("originalSizeBytes")?.N, out var os) ? os : 0,
            CompressedSizeBytes = long.TryParse(item.GetValueOrDefault("compressedSizeBytes")?.N, out var cs) ? cs : 0,
            SafeWindowThreshold = item.GetValueOrDefault("safeWindowThreshold")?.S ?? string.Empty,
            UpdatedAt = item.GetValueOrDefault("updatedAt")?.S ?? string.Empty,
            CreatedAt = item.GetValueOrDefault("createdAt")?.S ?? string.Empty,
            BuildSource = item.GetValueOrDefault("buildSource")?.S ?? string.Empty,
            BuildHost = item.GetValueOrDefault("buildHost")?.S
        };
    }

    /// <summary>
    ///     Creates from a MultiProjectionStateRecord.
    /// </summary>
    public static DynamoMultiProjectionState FromRecord(MultiProjectionStateRecord record, string serviceId)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new DynamoMultiProjectionState
        {
            Pk = $"SERVICE#{serviceId}#PROJECTOR#{record.ProjectorName}",
            Sk = $"VERSION#{record.ProjectorVersion}",
            ServiceId = serviceId,
            ProjectorName = record.ProjectorName,
            ProjectorVersion = record.ProjectorVersion,
            PayloadType = record.PayloadType,
            LastSortableUniqueId = record.LastSortableUniqueId,
            EventsProcessed = record.EventsProcessed,
            StateData = record.StateData != null ? Convert.ToBase64String(record.StateData) : null,
            IsOffloaded = record.IsOffloaded,
            OffloadKey = record.OffloadKey,
            OffloadProvider = record.OffloadProvider,
            OriginalSizeBytes = record.OriginalSizeBytes,
            CompressedSizeBytes = record.CompressedSizeBytes,
            SafeWindowThreshold = record.SafeWindowThreshold,
            CreatedAt = record.CreatedAt.ToString("O"),
            UpdatedAt = record.UpdatedAt.ToString("O"),
            BuildSource = record.BuildSource,
            BuildHost = record.BuildHost
        };
    }

    /// <summary>
    ///     Converts to a MultiProjectionStateRecord.
    /// </summary>
    public MultiProjectionStateRecord ToRecord(byte[]? overrideStateData = null)
    {
        var stateData = overrideStateData ?? (StateData != null ? Convert.FromBase64String(StateData) : null);
        var createdAt = DateTime.TryParse(CreatedAt, out var created) ? created : DateTime.UtcNow;
        var updatedAt = DateTime.TryParse(UpdatedAt, out var updated) ? updated : DateTime.UtcNow;

        return new MultiProjectionStateRecord(
            ProjectorName: ProjectorName,
            ProjectorVersion: ProjectorVersion,
            PayloadType: PayloadType,
            LastSortableUniqueId: LastSortableUniqueId,
            EventsProcessed: EventsProcessed,
            StateData: stateData,
            IsOffloaded: IsOffloaded,
            OffloadKey: OffloadKey,
            OffloadProvider: OffloadProvider,
            OriginalSizeBytes: OriginalSizeBytes,
            CompressedSizeBytes: CompressedSizeBytes,
            SafeWindowThreshold: SafeWindowThreshold,
            CreatedAt: createdAt,
            UpdatedAt: updatedAt,
            BuildSource: BuildSource,
            BuildHost: BuildHost);
    }
}

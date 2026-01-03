using Newtonsoft.Json;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.CosmosDb.Models;

/// <summary>
///     Represents multi projection state in CosmosDB.
///     PartitionKey: "MultiProjectionState_{ProjectorName}"
///     Id: ProjectorVersion
/// </summary>
public class CosmosMultiProjectionState
{
    /// <summary>
    ///     CosmosDB document ID (ProjectorVersion).
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Partition key.
    /// </summary>
    [JsonProperty("partitionKey")]
    public string PartitionKey { get; set; } = string.Empty;

    /// <summary>
    ///     Name of the projector.
    /// </summary>
    [JsonProperty("projectorName")]
    public string ProjectorName { get; set; } = string.Empty;

    /// <summary>
    ///     Version of the projector.
    /// </summary>
    [JsonProperty("projectorVersion")]
    public string ProjectorVersion { get; set; } = string.Empty;

    /// <summary>
    ///     Type of the payload.
    /// </summary>
    [JsonProperty("payloadType")]
    public string PayloadType { get; set; } = string.Empty;

    /// <summary>
    ///     Last sortable unique ID processed.
    /// </summary>
    [JsonProperty("lastSortableUniqueId")]
    public string LastSortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Number of events processed.
    /// </summary>
    [JsonProperty("eventsProcessed")]
    public long EventsProcessed { get; set; }

    /// <summary>
    ///     Base64 encoded gzip compressed state data.
    /// </summary>
    [JsonProperty("stateData")]
    public string? StateData { get; set; }

    /// <summary>
    ///     Whether the state data is offloaded to blob storage.
    /// </summary>
    [JsonProperty("isOffloaded")]
    public bool IsOffloaded { get; set; }

    /// <summary>
    ///     Key for the offloaded blob.
    /// </summary>
    [JsonProperty("offloadKey")]
    public string? OffloadKey { get; set; }

    /// <summary>
    ///     Provider name for the offloaded blob.
    /// </summary>
    [JsonProperty("offloadProvider")]
    public string? OffloadProvider { get; set; }

    /// <summary>
    ///     Original size in bytes before compression.
    /// </summary>
    [JsonProperty("originalSizeBytes")]
    public long OriginalSizeBytes { get; set; }

    /// <summary>
    ///     Compressed size in bytes.
    /// </summary>
    [JsonProperty("compressedSizeBytes")]
    public long CompressedSizeBytes { get; set; }

    /// <summary>
    ///     Safe window threshold for this state.
    /// </summary>
    [JsonProperty("safeWindowThreshold")]
    public string SafeWindowThreshold { get; set; } = string.Empty;

    /// <summary>
    ///     When the state was created.
    /// </summary>
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     When the state was last updated.
    /// </summary>
    [JsonProperty("updatedAt")]
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    ///     Source that built this state.
    /// </summary>
    [JsonProperty("buildSource")]
    public string BuildSource { get; set; } = string.Empty;

    /// <summary>
    ///     Host that built this state.
    /// </summary>
    [JsonProperty("buildHost")]
    public string? BuildHost { get; set; }

    /// <summary>
    ///     CosmosDB ETag for optimistic concurrency.
    /// </summary>
    [JsonProperty("_etag")]
    public string? ETag { get; set; }

    /// <summary>
    ///     Creates a CosmosMultiProjectionState from a MultiProjectionStateRecord.
    /// </summary>
    /// <param name="record">The record to convert.</param>
    /// <returns>A new CosmosMultiProjectionState instance.</returns>
    public static CosmosMultiProjectionState FromRecord(MultiProjectionStateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return new CosmosMultiProjectionState
        {
            Id = record.ProjectorVersion,
            PartitionKey = record.GetPartitionKey(),
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
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            BuildSource = record.BuildSource,
            BuildHost = record.BuildHost
        };
    }

    /// <summary>
    ///     Converts this CosmosMultiProjectionState to a MultiProjectionStateRecord.
    /// </summary>
    /// <param name="overrideStateData">Optional state data to use instead of the stored data.</param>
    /// <returns>A new MultiProjectionStateRecord instance.</returns>
    public MultiProjectionStateRecord ToRecord(byte[]? overrideStateData = null)
    {
        var stateData = overrideStateData ?? (StateData != null ? Convert.FromBase64String(StateData) : null);

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
            CreatedAt: CreatedAt,
            UpdatedAt: UpdatedAt,
            BuildSource: BuildSource,
            BuildHost: BuildHost);
    }
}

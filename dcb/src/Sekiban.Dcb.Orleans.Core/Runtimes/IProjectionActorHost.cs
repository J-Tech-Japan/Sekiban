using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Serialization;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Grain-facing projection host abstraction.
///     Engine-agnostic: operates on SerializableEvent, SerializableQuery*, byte[] snapshots,
///     and primitive metadata only. No DcbDomainTypes, JsonSerializerOptions, or IServiceProvider.
/// </summary>
public interface IProjectionActorHost
{
    /// <summary>
    ///     Ingest a batch of serializable events into the projection.
    /// </summary>
    Task AddSerializableEventsAsync(
        IReadOnlyList<SerializableEvent> events,
        bool finishedCatchUp = true);

    /// <summary>
    ///     Ingest a batch of deserialized events (e.g. from EventStore catch-up).
    ///     The host converts them internally; the Grain does not need DcbDomainTypes.
    /// </summary>
    Task AddEventsFromCatchUpAsync(
        IReadOnlyList<Event> events,
        bool finishedCatchUp = true);

    /// <summary>
    ///     Get primitive metadata about the projection state.
    /// </summary>
    Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(bool includeUnsafe = true);

    /// <summary>
    ///     Get the full projection state (including domain payload).
    ///     The Grain passes this through opaquely to callers.
    /// </summary>
    Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Get the snapshot as opaque bytes. Format is owned by the implementation.
    /// </summary>
    Task<ResultBox<byte[]>> GetSnapshotBytesAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Restore projection state from opaque snapshot bytes.
    /// </summary>
    Task<ResultBox<bool>> RestoreSnapshotAsync(byte[] snapshotData);

    /// <summary>
    ///     Execute a single-result query. The host deserializes the query and
    ///     serializes the result internally.
    /// </summary>
    Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion);

    /// <summary>
    ///     Execute a list query. The host deserializes the query and
    ///     serializes the result internally.
    /// </summary>
    Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        SerializableQueryParameter query,
        int? safeVersion,
        string? safeThreshold,
        DateTime? safeThresholdTime,
        int? unsafeVersion);

    /// <summary>
    ///     Force promotion of buffered events within the safe window.
    /// </summary>
    void ForcePromoteBufferedEvents();

    /// <summary>
    ///     Force promotion of ALL buffered events regardless of window.
    /// </summary>
    void ForcePromoteAllBufferedEvents();

    /// <summary>
    ///     Get the last safe (committed) sortable unique ID.
    /// </summary>
    Task<string> GetSafeLastSortableUniqueIdAsync();

    /// <summary>
    ///     Check whether a specific sortable unique ID has been received.
    /// </summary>
    Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId);

    /// <summary>
    ///     Estimate the in-memory state size in bytes.
    /// </summary>
    long EstimateStateSizeBytes(bool includeUnsafeDetails);

    /// <summary>
    ///     Peek at the current safe window threshold without mutating state.
    /// </summary>
    string PeekCurrentSafeWindowThreshold();

    /// <summary>
    ///     Get the projector version string.
    /// </summary>
    string GetProjectorVersion();

    /// <summary>
    ///     Rewrite the projector version in a serialized snapshot.
    ///     Returns updated snapshot bytes.
    /// </summary>
    byte[] RewriteSnapshotVersion(byte[] snapshotData, string newVersion);
}

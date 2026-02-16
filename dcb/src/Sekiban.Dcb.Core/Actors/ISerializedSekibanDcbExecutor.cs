using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Serialized command executor contract for WASM local/remote execution.
///     Intentionally separate from ISekibanExecutor — this is a WASM boundary interface
///     that operates on serialized payloads rather than typed commands/queries.
/// </summary>
public interface ISerializedSekibanDcbExecutor
{
    /// <summary>
    ///     Gets the serializable tag state for a given tag state ID.
    ///     Returns the raw serialized state without deserialization.
    /// </summary>
    Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId);

    /// <summary>
    ///     Commits serialized events with consistency reservation flow.
    ///     Server generates metadata (EventId, SortableUniqueId, EventMetadata)
    ///     and performs reservation → write → confirm/cancel.
    /// </summary>
    Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default);
}

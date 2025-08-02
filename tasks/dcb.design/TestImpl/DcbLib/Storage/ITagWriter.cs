using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Storage;

/// <summary>
/// Writes tag metadata to the event store.
/// Called by CommandExecutor after successful reservation to update tag versions.
/// </summary>
public interface ITagWriter
{
    /// <summary>
    /// Writes a tag stream update with version control
    /// </summary>
    /// <param name="tag">The tag to write</param>
    /// <param name="newVersion">The new version number</param>
    /// <param name="lastSortableUniqueId">The last sortable unique ID for this tag</param>
    /// <param name="expectedVersion">Expected current version for optimistic concurrency</param>
    /// <returns>ResultBox containing the TagWriteResult or error</returns>
    Task<ResultBox<TagWriteResult>> WriteTagAsync(
        ITag tag, 
        long newVersion, 
        string lastSortableUniqueId,
        long expectedVersion);

    /// <summary>
    /// Writes multiple tag updates in a batch (atomic operation)
    /// </summary>
    /// <param name="tagWrites">Collection of tag write operations</param>
    /// <returns>ResultBox containing list of TagWriteResults or error</returns>
    Task<ResultBox<IReadOnlyList<TagWriteResult>>> WriteTagsAsync(
        IEnumerable<TagWriteOperation> tagWrites);

    /// <summary>
    /// Updates tag metadata without changing version (for non-event updates)
    /// </summary>
    /// <param name="tag">The tag to update</param>
    /// <param name="metadata">Metadata to update</param>
    /// <returns>ResultBox indicating success or error</returns>
    Task<ResultBox<UnitValue>> UpdateTagMetadataAsync(
        ITag tag,
        Dictionary<string, object> metadata);
}

/// <summary>
/// Represents a single tag write operation for batch writes
/// </summary>
public record TagWriteOperation(
    ITag Tag,
    long NewVersion,
    string LastSortableUniqueId,
    long ExpectedVersion
);
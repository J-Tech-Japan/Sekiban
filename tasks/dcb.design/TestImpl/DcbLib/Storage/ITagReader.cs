using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Storage;

/// <summary>
/// Reads tag metadata from the event store.
/// Used by TagConsistentActor when starting from idle to get current tag state.
/// </summary>
public interface ITagReader
{
    /// <summary>
    /// Reads the current tag stream information for a specific tag
    /// </summary>
    /// <param name="tag">The tag to read</param>
    /// <returns>ResultBox containing the TagStream or error</returns>
    Task<ResultBox<TagStream>> ReadTagStreamAsync(ITag tag);

    /// <summary>
    /// Reads multiple tag streams in a batch
    /// </summary>
    /// <param name="tags">The tags to read</param>
    /// <returns>Dictionary of tag to TagStream mappings</returns>
    Task<Dictionary<ITag, TagStream>> ReadTagStreamsAsync(IEnumerable<ITag> tags);

    /// <summary>
    /// Gets the latest version number for a tag
    /// </summary>
    /// <param name="tag">The tag to check</param>
    /// <returns>The latest version number or 0 if not found</returns>
    Task<long> GetLatestVersionAsync(ITag tag);

    /// <summary>
    /// Checks if a tag exists in the store
    /// </summary>
    /// <param name="tag">The tag to check</param>
    /// <returns>True if the tag exists</returns>
    Task<bool> ExistsAsync(ITag tag);
}
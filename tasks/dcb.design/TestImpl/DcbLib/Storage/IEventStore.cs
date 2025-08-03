using DcbLib.Common;
using DcbLib.Events;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Storage;

/// <summary>
/// Unified interface for event and tag storage operations
/// </summary>
public interface IEventStore
{
    // Event operations
    
    /// <summary>
    /// Reads all events from the event store
    /// </summary>
    /// <param name="since">Optional: Only return events after this ID</param>
    Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(SortableUniqueId? since = null);
    
    /// <summary>
    /// Reads events for a specific tag
    /// </summary>
    /// <param name="tag">The tag to filter events by</param>
    /// <param name="since">Optional: Only return events after this ID</param>
    Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(ITag tag, SortableUniqueId? since = null);
    
    /// <summary>
    /// Reads a specific event by ID
    /// </summary>
    Task<ResultBox<Event>> ReadEventAsync(Guid eventId);
    
    /// <summary>
    /// Writes an event to the event store
    /// </summary>
    Task<ResultBox<Guid>> WriteEventAsync(Event evt);
    
    // Tag operations
    
    /// <summary>
    /// Reads tag streams for a specific tag
    /// </summary>
    Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag);
    
    /// <summary>
    /// Gets the latest tag state
    /// </summary>
    Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag);
    
    /// <summary>
    /// Checks if a tag exists
    /// </summary>
    Task<ResultBox<bool>> TagExistsAsync(ITag tag);
    
    /// <summary>
    /// Writes a new tag state
    /// </summary>
    Task<ResultBox<TagWriteResult>> WriteTagAsync(ITag tag, TagState state);
}
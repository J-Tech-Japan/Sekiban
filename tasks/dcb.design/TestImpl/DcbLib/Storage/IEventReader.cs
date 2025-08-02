using DcbLib.Events;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Storage;

/// <summary>
/// Reads events from the event store.
/// Used by TagStateActor to reconstruct state from events.
/// </summary>
public interface IEventReader
{
    /// <summary>
    /// Reads all events for a specific tag
    /// </summary>
    /// <param name="tag">The tag to read events for</param>
    /// <param name="fromPosition">Optional starting position (exclusive)</param>
    /// <param name="maxCount">Maximum number of events to read</param>
    /// <returns>ResultBox containing list of events or error</returns>
    Task<ResultBox<IReadOnlyList<Event>>> ReadEventsForTagAsync(
        ITag tag,
        long? fromPosition = null,
        int? maxCount = null);

    /// <summary>
    /// Reads events for multiple tags
    /// </summary>
    /// <param name="tags">The tags to read events for</param>
    /// <param name="fromPosition">Optional starting position (exclusive)</param>
    /// <param name="maxCount">Maximum number of events to read per tag</param>
    /// <returns>Dictionary mapping tags to their events</returns>
    Task<Dictionary<ITag, IReadOnlyList<Event>>> ReadEventsForTagsAsync(
        IEnumerable<ITag> tags,
        long? fromPosition = null,
        int? maxCount = null);

    /// <summary>
    /// Reads events from the global stream
    /// </summary>
    /// <param name="fromPosition">Starting position (exclusive)</param>
    /// <param name="count">Number of events to read</param>
    /// <returns>ResultBox containing list of events or error</returns>
    Task<ResultBox<IReadOnlyList<Event>>> ReadGlobalEventsAsync(
        long fromPosition,
        int count);

    /// <summary>
    /// Gets the latest event position for a tag
    /// </summary>
    /// <param name="tag">The tag to check</param>
    /// <returns>The position of the latest event or -1 if no events</returns>
    Task<long> GetLatestEventPositionAsync(ITag tag);

    /// <summary>
    /// Subscribes to real-time events for specific tags
    /// </summary>
    /// <param name="tags">Tags to subscribe to</param>
    /// <param name="fromPosition">Position to start subscription from</param>
    /// <param name="onEvent">Callback for each new event</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when subscription ends</returns>
    Task SubscribeToEventsAsync(
        IEnumerable<ITag> tags,
        long fromPosition,
        Func<Event, Task> onEvent,
        CancellationToken cancellationToken);
}
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Interface for projections that can efficiently manage both safe and unsafe states
///     without duplicating data. This allows for memory-efficient handling of large projections.
/// </summary>
/// <typeparam name="T">The type of the projection state</typeparam>
public interface ISafeAndUnsafeStateAccessor<T> where T : IMultiProjectionPayload
{
    /// <summary>
    ///     Gets the safe state (events outside the safe window, fully processed and ordered)
    /// </summary>
    /// <returns>The safe projection state</returns>
    T GetSafeState();

    /// <summary>
    ///     Gets the unsafe state (includes all events including those within the safe window)
    /// </summary>
    /// <param name="domainTypes">The domain types for tag parsing</param>
    /// <param name="timeProvider">The time provider for safe window calculations</param>
    /// <returns>The unsafe projection state</returns>
    T GetUnsafeState(DcbDomainTypes domainTypes, TimeProvider timeProvider);

    /// <summary>
    ///     Processes an event and returns the updated state
    /// </summary>
    /// <param name="evt">The event to process</param>
    /// <param name="safeWindowThreshold">The threshold for determining if an event is safe</param>
    /// <param name="domainTypes">The domain types for tag parsing</param>
    /// <param name="timeProvider">The time provider for safe window calculations</param>
    /// <returns>The updated state</returns>
    ISafeAndUnsafeStateAccessor<T> ProcessEvent(Event evt, SortableUniqueId safeWindowThreshold, DcbDomainTypes domainTypes, TimeProvider timeProvider);

    /// <summary>
    ///     Gets the last processed event ID
    /// </summary>
    Guid GetLastEventId();

    /// <summary>
    ///     Gets the last processed sortable unique ID
    /// </summary>
    string GetLastSortableUniqueId();

    /// <summary>
    ///     Gets the version number of the state
    /// </summary>
    int GetVersion();
}

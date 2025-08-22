using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;

namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
/// Orleans grain interface for multi-projection
/// </summary>
public interface IMultiProjectionGrain : IGrainWithStringKey
{
    /// <summary>
    /// Get the current state of the multi-projection
    /// </summary>
    /// <param name="canGetUnsafeState">Whether to return unsafe state (default: true)</param>
    /// <returns>The current multi-projection state</returns>
    Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true);
    
    /// <summary>
    /// Get the serializable state for persistence
    /// </summary>
    /// <param name="canGetUnsafeState">Whether to return unsafe state (default: true)</param>
    /// <returns>The serializable multi-projection state</returns>
    Task<ResultBox<Orleans.MultiProjections.SerializableMultiProjectionStateDto>> GetSerializableStateAsync(bool canGetUnsafeState = true);
    
    /// <summary>
    /// Manually add events to the projection (mainly for testing)
    /// </summary>
    /// <param name="events">Events to add</param>
    /// <param name="finishedCatchUp">Whether catch-up is complete</param>
    Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true);
    
    /// <summary>
    /// Get the status of the grain
    /// </summary>
    /// <returns>Status information</returns>
    Task<MultiProjectionGrainStatus> GetStatusAsync();
    
    /// <summary>
    /// Force persist the current state
    /// </summary>
    /// <returns>Success (true) or error</returns>
    Task<ResultBox<bool>> PersistStateAsync();
    
    /// <summary>
    /// Stop the event subscription
    /// </summary>
    Task StopSubscriptionAsync();
    
    /// <summary>
    /// Start or restart the event subscription
    /// </summary>
    Task StartSubscriptionAsync();
    
    /// <summary>
    /// Execute a single-result query against the projection
    /// </summary>
    /// <param name="query">The query to execute</param>
    /// <returns>The query result wrapped in QueryResultGeneral for serialization</returns>
    Task<QueryResultGeneral> ExecuteQueryAsync(IQueryCommon query);
    
    /// <summary>
    /// Execute a list query against the projection
    /// </summary>
    /// <param name="query">The list query to execute</param>
    /// <returns>The paginated query result wrapped in ListQueryResultGeneral for serialization</returns>
    Task<ListQueryResultGeneral> ExecuteListQueryAsync(IListQueryCommon query);
    
    /// <summary>
    /// Check if a specific sortable unique ID has been received and processed.
    /// </summary>
    /// <param name="sortableUniqueId">The sortable unique ID to check for.</param>
    /// <returns>True if the event with this ID has been processed, false otherwise.</returns>
    Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);
    
    /// <summary>
    /// Manually refresh the projection by catching up from the event store
    /// </summary>
    Task RefreshAsync();
}

/// <summary>
/// Status information for the multi-projection grain
/// </summary>
[GenerateSerializer]
public record MultiProjectionGrainStatus(
    [property: Id(0)] string ProjectorName,
    [property: Id(1)] bool IsSubscriptionActive,
    [property: Id(2)] bool IsCaughtUp,
    [property: Id(3)] string? CurrentPosition,
    [property: Id(4)] long EventsProcessed,
    [property: Id(5)] DateTime? LastEventTime,
    [property: Id(6)] DateTime? LastPersistTime,
    [property: Id(7)] long StateSize,
    [property: Id(8)] bool HasError,
    [property: Id(9)] string? LastError);

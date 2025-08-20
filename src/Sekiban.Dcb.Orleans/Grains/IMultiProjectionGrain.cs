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
    Task<ResultBox<SerializableMultiProjectionState>> GetSerializableStateAsync(bool canGetUnsafeState = true);
    
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
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="query">The query to execute</param>
    /// <returns>The query result</returns>
    Task<ResultBox<object>> ExecuteQueryAsync(IQueryCommon query);
    
    /// <summary>
    /// Execute a list query against the projection
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="query">The list query to execute</param>
    /// <returns>The paginated query result</returns>
    Task<ResultBox<object>> ExecuteListQueryAsync(IListQueryCommon query);
}

/// <summary>
/// Status information for the multi-projection grain
/// </summary>
public record MultiProjectionGrainStatus(
    string ProjectorName,
    bool IsSubscriptionActive,
    bool IsCaughtUp,
    string? CurrentPosition,
    long EventsProcessed,
    DateTime? LastEventTime,
    DateTime? LastPersistTime,
    long StateSize,
    bool HasError,
    string? LastError);
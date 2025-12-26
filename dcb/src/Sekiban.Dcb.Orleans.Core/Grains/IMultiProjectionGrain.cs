using ResultBoxes;
using Orleans.Concurrency;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Snapshots;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Orleans.Serialization;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Orleans grain interface for multi-projection
/// </summary>
public interface IMultiProjectionGrain : IGrainWithStringKey
{
    /// <summary>
    ///     Get the current state of the multi-projection
    /// </summary>
    /// <param name="canGetUnsafeState">Whether to return unsafe state (default: true)</param>
    /// <returns>The current multi-projection state</returns>
    Task<ResultBox<MultiProjectionState>> GetStateAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Get snapshot envelope (inline or offloaded) as JSON according to actor policy.
    /// </summary>
    Task<ResultBox<string>> GetSnapshotJsonAsync(bool canGetUnsafeState = true);

    /// <summary>
    ///     Manually add events to the projection (mainly for testing)
    /// </summary>
    /// <param name="events">Events to add</param>
    /// <param name="finishedCatchUp">Whether catch-up is complete</param>
    Task AddEventsAsync(IReadOnlyList<Event> events, bool finishedCatchUp = true);

    /// <summary>
    ///     Get the status of the grain
    /// </summary>
    /// <returns>Status information</returns>
    [AlwaysInterleave]
    Task<MultiProjectionGrainStatus> GetStatusAsync();

    /// <summary>
    ///     Force persist the current state
    /// </summary>
    /// <returns>Success (true) or error</returns>
    Task<ResultBox<bool>> PersistStateAsync();

    /// <summary>
    ///     Stop the event subscription
    /// </summary>
    Task StopSubscriptionAsync();

    /// <summary>
    ///     Start or restart the event subscription
    /// </summary>
    Task StartSubscriptionAsync();

    /// <summary>
    ///     Execute a single-result query against the projection
    /// </summary>
    /// <param name="query">The query to execute</param>
    /// <returns>The query result wrapped in QueryResultGeneral for serialization</returns>
    Task<SerializableQueryResult> ExecuteQueryAsync(SerializableQueryParameter query);

    /// <summary>
    ///     Execute a list query against the projection
    /// </summary>
    /// <param name="query">The list query to execute</param>
    /// <returns>The paginated query result wrapped in ListQueryResultGeneral for serialization</returns>
    Task<SerializableListQueryResult> ExecuteListQueryAsync(SerializableQueryParameter query);

    /// <summary>
    ///     Check if a specific sortable unique ID has been received and processed.
    /// </summary>
    /// <param name="sortableUniqueId">The sortable unique ID to check for.</param>
    /// <returns>True if the event with this ID has been processed, false otherwise.</returns>
    Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);

    /// <summary>
    ///     Manually refresh the projection by catching up from the event store
    /// </summary>
    Task RefreshAsync();

    /// <summary>
    ///     Testing aid: request deactivation to simulate restart/activation cycle.
    /// </summary>
    Task RequestDeactivationAsync();

    /// <summary>
    ///     Testing aid: overwrite persisted snapshot's ProjectorVersion to simulate mismatch.
    ///     Returns false if there is no persisted state to overwrite.
    /// </summary>
    Task<bool> OverwritePersistedStateVersionAsync(string newVersion);

    /// <summary>
    ///     Testing aid: seed events into the backing event store to support catch-up tests.
    /// </summary>
    Task SeedEventsAsync(IReadOnlyList<Event> events);

    /// <summary>
    ///     Get event delivery statistics for debugging duplicate/missing events
    /// </summary>
    [AlwaysInterleave]
    Task<EventDeliveryStatistics> GetEventDeliveryStatisticsAsync();

    /// <summary>
    ///     Get catch-up progress/status for operational checks.
    /// </summary>
    Task<MultiProjectionCatchUpStatus> GetCatchUpStatusAsync();
}

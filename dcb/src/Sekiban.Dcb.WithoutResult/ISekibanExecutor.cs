using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb;

/// <summary>
///     Main executor interface for Sekiban DCB (WithoutResult version)
///     Exception-based error handling - operations throw exceptions on failure
/// </summary>
public interface ISekibanExecutor : ICommandExecutor
{
    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    /// <param name="tagStateId">The tag state identifier</param>
    /// <returns>The tag state</returns>
    /// <exception cref="Exception">Thrown when tag state cannot be retrieved</exception>
    Task<TagState> GetTagStateAsync(TagStateId tagStateId);

    Task<TagState> GetTagStateAsync<TProjector>(ITag tag)
        where TProjector : ITagProjector<TProjector> =>
        GetTagStateAsync(TagStateId.FromProjector<TProjector>(tag));

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="queryCommon">The query to execute</param>
    /// <returns>The query result</returns>
    /// <exception cref="Exception">Thrown when query execution fails</exception>
    Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon)
        where TResult : notnull;

    /// <summary>
    ///     Execute a list query with pagination support
    /// </summary>
    /// <typeparam name="TResult">The type of items in the result list</typeparam>
    /// <param name="queryCommon">The list query to execute</param>
    /// <returns>The paginated query result</returns>
    /// <exception cref="Exception">Thrown when query execution fails</exception>
    Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull;

    /// <summary>
    ///     Gets the latest (maximum) SortableUniqueId across all events in the event store.
    ///     Useful for passing to IWaitForSortableUniqueId to ensure projection catch-up.
    ///     Returns empty string if no events exist.
    /// </summary>
    /// <exception cref="Exception">Thrown when the event store query fails</exception>
    Task<string> GetLatestSortableUniqueIdAsync();

    /// <summary>
    ///     Gets projection head/catch-up status for a specific multi-projector type.
    /// </summary>
    Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync<TProjector>()
        where TProjector : IMultiProjector<TProjector> =>
        GetProjectionHeadStatusAsync(TProjector.MultiProjectorName, TProjector.MultiProjectorVersion);

    /// <summary>
    ///     Gets projection head/catch-up status for a specific projector name.
    ///     When expectedProjectorVersion is provided, the executor validates it against the registered projector version.
    ///     Backends without background catch-up may still report `CatchUp.IsInProgress == false`;
    ///     compare `Current` and `Consistent` to detect safe-window lag in that case.
    /// </summary>
    Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null);

    /// <summary>
    ///     Gets the global event-store head.
    ///     TotalEventCount is opt-in because providers such as Postgres/SQLite may execute `COUNT(*)`,
    ///     while Cosmos DB or DynamoDB may require additional query work across partitions or shards.
    /// </summary>
    Task<EventStoreHeadStatus> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false);
}

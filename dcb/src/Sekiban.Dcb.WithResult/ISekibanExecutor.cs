using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb;

/// <summary>
///     Main executor interface for Sekiban DCB
///     Combines command execution with tag state retrieval and query capabilities
/// </summary>
public interface ISekibanExecutor : ICommandExecutor
{
    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    /// <param name="tagStateId">The tag state identifier</param>
    /// <returns>ResultBox containing the tag state or error</returns>
    Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId);
    Task<ResultBox<TagState>> GetTagStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector> => GetTagStateAsync(TagStateId.FromProjector<TProjector>(tag));

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="queryCommon">The query to execute</param>
    /// <returns>The query result</returns>
    Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull;

    /// <summary>
    ///     Execute a list query with pagination support
    /// </summary>
    /// <typeparam name="TResult">The type of items in the result list</typeparam>
    /// <param name="queryCommon">The list query to execute</param>
    /// <returns>The paginated query result</returns>
    Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull;

    /// <summary>
    ///     Gets the latest (maximum) SortableUniqueId across all events in the event store.
    ///     Useful for passing to IWaitForSortableUniqueId to ensure projection catch-up.
    ///     Returns empty string if no events exist.
    /// </summary>
    Task<ResultBox<string>> GetLatestSortableUniqueIdAsync();

    /// <summary>
    ///     Gets projection head/catch-up status for a specific multi-projector type.
    /// </summary>
    Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync<TProjector>()
        where TProjector : IMultiProjector<TProjector> =>
        GetProjectionHeadStatusAsync(TProjector.MultiProjectorName, TProjector.MultiProjectorVersion);

    /// <summary>
    ///     Gets projection head/catch-up status for a specific projector name.
    ///     When expectedProjectorVersion is provided, the executor validates it against the registered projector version.
    ///     Backends without background catch-up may still report `CatchUp.IsInProgress == false`;
    ///     compare `Current` and `Consistent` to detect safe-window lag in that case.
    /// </summary>
    Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null);

    /// <summary>
    ///     Gets the global event-store head.
    ///     TotalEventCount is opt-in because providers such as Postgres/SQLite may execute `COUNT(*)`,
    ///     while Cosmos DB or DynamoDB may require additional query work across partitions or shards.
    /// </summary>
    Task<ResultBox<EventStoreHeadStatus>> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false);
}

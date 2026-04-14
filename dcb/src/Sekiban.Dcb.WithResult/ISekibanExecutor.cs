using ResultBoxes;
using Sekiban.Dcb.Commands;
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
    ///     Get the latest SortableUniqueId committed for any tag in the specified tag group.
    ///     Queries the event store directly, so the result reflects the latest committed state.
    /// </summary>
    /// <param name="tagGroup">The tag group name to query</param>
    /// <returns>The latest SortableUniqueId string, or empty string if no events exist for the tag group</returns>
    Task<ResultBox<string>> GetLatestSortableUniqueIdForTagGroupAsync(string tagGroup);

    /// <summary>
    ///     Type-safe overload that derives the tag group name from TTagGroup.
    /// </summary>
    Task<ResultBox<string>> GetLatestSortableUniqueIdForTagGroupAsync<TTagGroup>()
        where TTagGroup : ITagGroup<TTagGroup>
        => GetLatestSortableUniqueIdForTagGroupAsync(TTagGroup.TagGroupName);
}

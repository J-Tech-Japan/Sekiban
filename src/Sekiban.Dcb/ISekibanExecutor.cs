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
}

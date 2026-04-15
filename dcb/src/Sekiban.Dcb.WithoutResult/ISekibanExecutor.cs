using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
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
    ///     Gets the lexicographically maximum tag string within a tag group.
    /// </summary>
    /// <param name="tagGroup">The tag group name (e.g. "Student")</param>
    /// <returns>The full tag string (e.g. "Student:01HX999") or empty string if none exist</returns>
    /// <exception cref="Exception">Thrown when retrieval fails</exception>
    Task<string> GetMaxTagInTagGroupAsync(string tagGroup);

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
}

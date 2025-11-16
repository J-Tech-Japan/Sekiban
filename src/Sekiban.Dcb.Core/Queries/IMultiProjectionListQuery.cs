using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Core interface for multi-projection queries that return a list of results (ResultBox-based)
/// </summary>
/// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
/// <typeparam name="TQuery">The query type</typeparam>
/// <typeparam name="TOutput">The output type of each item in the list</typeparam>
public interface
    ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> : IListQueryCommon<TQuery, TOutput>,
    IQueryPagingParameter
    where TMultiProjector : ICoreMultiProjector<TMultiProjector>
    where TQuery : ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    /// <summary>
    ///     Filter the projection to get the items
    /// </summary>
    /// <param name="projector">The multi-projector state</param>
    /// <param name="query">The query instance</param>
    /// <param name="context">The query context</param>
    /// <returns>The filtered items</returns>
    static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context);

    /// <summary>
    ///     Sort the filtered items
    /// </summary>
    /// <param name="filteredList">The filtered items</param>
    /// <param name="query">The query instance</param>
    /// <param name="context">The query context</param>
    /// <returns>The sorted items</returns>
    static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}

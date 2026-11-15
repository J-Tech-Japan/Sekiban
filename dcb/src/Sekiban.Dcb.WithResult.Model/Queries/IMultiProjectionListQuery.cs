using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Interface for multi-projection queries that return a list of results (ResultBox-based error handling).
///     The exception-based core contract
///     <see cref="ICoreMultiProjectionListQuery{TMultiProjector,TQuery,TOutput}" /> is implemented here by
///     unwrapping the returned boxes once.
/// </summary>
public interface IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> : ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>
    where TMultiProjector : ICoreMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    /// <summary>
    ///     Filter the projection to get the items
    /// </summary>
    /// <param name="projector">The multi-projector state</param>
    /// <param name="query">The query instance</param>
    /// <param name="context">The query context</param>
    /// <returns>The filtered items</returns>
    new static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
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
    new static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);

    static IEnumerable<TOutput> ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>.HandleFilter(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context) =>
        ResultBoxBridge.Unwrap(TQuery.HandleFilter(projector, query, context));

    static IEnumerable<TOutput> ICoreMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>.HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context) =>
        ResultBoxBridge.Unwrap(TQuery.HandleSort(filteredList, query, context));
}

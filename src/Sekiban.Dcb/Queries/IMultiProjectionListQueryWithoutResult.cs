using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Queries;

/// <summary>
///     Multi-projection list query variant that returns collections directly without ResultBox.
/// </summary>
/// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
/// <typeparam name="TQuery">The query type</typeparam>
/// <typeparam name="TOutput">The item output type</typeparam>
public interface IMultiProjectionListQueryWithoutResult<TMultiProjector, TQuery, TOutput> :
    IListQueryCommon<TQuery, TOutput>,
    IQueryPagingParameter
    where TMultiProjector : IMultiProjectorWithoutResult<TMultiProjector>
    where TQuery : IMultiProjectionListQueryWithoutResult<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    static abstract IEnumerable<TOutput> HandleFilter(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context);

    static abstract IEnumerable<TOutput> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
public interface IMultiProjectionListQueryWithoutResult2<TMultiProjector, TQuery, TOutput> :
    IListQueryCommon<TQuery, TOutput>,
    IQueryPagingParameter
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQueryWithoutResult2<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    static abstract IEnumerable<TOutput> HandleFilter(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context);

    static abstract IEnumerable<TOutput> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}

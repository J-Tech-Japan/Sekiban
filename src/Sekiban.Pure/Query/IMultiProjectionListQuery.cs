using ResultBoxes;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Query;

public interface
    IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput> : IMultiProjectionQueryCommon<TMultiProjector>,
    IListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionListQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
{
    public static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(
        MultiProjectionState<TMultiProjector> projection,
        TQuery query,
        IQueryContext context);
    public static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}

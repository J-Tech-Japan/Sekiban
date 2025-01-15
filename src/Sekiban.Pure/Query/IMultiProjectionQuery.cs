using ResultBoxes;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Query;

public interface
    IMultiProjectionQuery<TMultiProjector, TQuery, TOutput> : IMultiProjectionQueryCommon<TMultiProjector>,
    IQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
{
    public static abstract ResultBox<TOutput> HandleQuery(
        MultiProjectionState<TMultiProjector> projection,
        TQuery query,
        IQueryContext context);
}

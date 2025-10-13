using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Queries;

/// <summary>
///     Multi-projection query variant that returns a value directly instead of wrapping in ResultBox.
/// </summary>
/// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
/// <typeparam name="TQuery">The query type</typeparam>
/// <typeparam name="TOutput">The output type</typeparam>
public interface IMultiProjectionQueryWithoutResult<TMultiProjector, TQuery, TOutput> :
    IQueryCommon<TQuery, TOutput>
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionQueryWithoutResult<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    static abstract TOutput HandleQuery(TMultiProjector projector, TQuery query, IQueryContext context);
}

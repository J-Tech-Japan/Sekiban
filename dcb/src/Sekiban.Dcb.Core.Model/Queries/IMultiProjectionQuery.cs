using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Core interface for multi-projection queries that return a single result (ResultBox-based)
/// </summary>
/// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
/// <typeparam name="TQuery">The query type</typeparam>
/// <typeparam name="TOutput">The output type of the query</typeparam>
public interface ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput> : IQueryCommon<TQuery, TOutput>
    where TMultiProjector : ICoreMultiProjector<TMultiProjector>
    where TQuery : ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    /// <summary>
    ///     Handle the query execution
    /// </summary>
    /// <param name="projector">The multi-projector state</param>
    /// <param name="query">The query instance</param>
    /// <param name="context">The query context</param>
    /// <returns>The query result</returns>
    static abstract ResultBox<TOutput> HandleQuery(TMultiProjector projector, TQuery query, IQueryContext context);
}

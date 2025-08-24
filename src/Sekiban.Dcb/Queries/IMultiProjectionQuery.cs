using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Interface for multi-projection queries that return a single result
/// </summary>
/// <typeparam name="TMultiProjector">The multi-projector type</typeparam>
/// <typeparam name="TQuery">The query type</typeparam>
/// <typeparam name="TOutput">The output type of the query</typeparam>
public interface IMultiProjectionQuery<TMultiProjector, TQuery, TOutput> : IQueryCommon<TQuery, TOutput>
    where TMultiProjector : IMultiProjector<TMultiProjector>
    where TQuery : IMultiProjectionQuery<TMultiProjector, TQuery, TOutput>, IEquatable<TQuery>
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

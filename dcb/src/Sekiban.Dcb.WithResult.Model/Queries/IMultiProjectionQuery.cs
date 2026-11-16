using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Queries;

/// <summary>
///     Interface for multi-projection queries that return a single result (ResultBox-based error handling).
///     The exception-based core contract <see cref="ICoreMultiProjectionQuery{TMultiProjector,TQuery,TOutput}" />
///     is implemented here by unwrapping the returned box once.
/// </summary>
public interface IMultiProjectionQuery<TMultiProjector, TQuery, TOutput> : ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput>
    where TMultiProjector : ICoreMultiProjector<TMultiProjector>
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
    new static abstract ResultBox<TOutput> HandleQuery(TMultiProjector projector, TQuery query, IQueryContext context);

    static TOutput ICoreMultiProjectionQuery<TMultiProjector, TQuery, TOutput>.HandleQuery(
        TMultiProjector projector,
        TQuery query,
        IQueryContext context) =>
        ResultBoxBridge.Unwrap(TQuery.HandleQuery(projector, query, context), ResultBoxBridge.NullQueryResultMessage);
}

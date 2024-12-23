using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.ISingleProjection;
namespace Sekiban.Core.Cache;

/// <summary>
///     defines a interface for multi projection Cache
///     Default implementation is <see cref="SingleProjectionCache" />
///     Application developer can implement this interface to provide custom cache implementation
/// </summary>
public interface ISingleProjectionCache
{
    /// <summary>
    ///     Set container to cache
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="container"></param>
    /// <typeparam name="TAggregate"></typeparam>
    /// <typeparam name="TState"></typeparam>
    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateStateCommon;

    /// <summary>
    ///     Get container from cache
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <typeparam name="TAggregate"></typeparam>
    /// <typeparam name="TState"></typeparam>
    /// <returns></returns>
    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateStateCommon;
}

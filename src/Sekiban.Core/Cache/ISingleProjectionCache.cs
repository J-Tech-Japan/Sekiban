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
    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container)
        where TAggregate : IAggregateCommon, ISingleProjection
        where TState : IAggregateCommon;

    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateCommon;
}

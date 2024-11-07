using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Memory Cache Projection Container.
/// </summary>
/// <typeparam name="TAggregate"></typeparam>
/// <typeparam name="TState"></typeparam>
// ReSharper disable once UnusedTypeParameter
public record SingleMemoryCacheProjectionContainer<TAggregate, TState> : IMemoryCacheContainer
    where TAggregate : IAggregateCommon, SingleProjections.ISingleProjection where TState : IAggregateStateCommon
{
    public Guid AggregateId { get; init; } = Guid.Empty;
    public List<IEvent> UnsafeEvents { get; init; } = [];
    public TState? State { get; init; } = default;
    public TState? SafeState { get; init; } = default;
    public DateTime CachedAt { get; init; } = SekibanDateProducer.GetRegistered().UtcNow;
    public SortableUniqueIdValue? LastSortableUniqueId { get; init; } = null;
    public SortableUniqueIdValue? SafeSortableUniqueId { get; init; } = null;
}
public interface IMemoryCacheContainer
{
    public SortableUniqueIdValue? SafeSortableUniqueId { get; }
}

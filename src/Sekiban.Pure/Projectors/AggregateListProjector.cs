using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.Collections.Immutable;
namespace Sekiban.Pure.Projectors;

public record AggregateListProjector<TAggregateProjector>(ImmutableDictionary<PartitionKeys, Aggregate> Aggregates)
    : IMultiProjector<AggregateListProjector<TAggregateProjector>>
    where TAggregateProjector : IAggregateProjector, new()
{
    public static AggregateListProjector<TAggregateProjector> GenerateInitialPayload() =>
        new(ImmutableDictionary<PartitionKeys, Aggregate>.Empty);
    public ResultBox<AggregateListProjector<TAggregateProjector>> Project(
        AggregateListProjector<TAggregateProjector> payload,
        IEvent ev)
    {
        var projector = new TAggregateProjector();
        var partitionKeys = ev.PartitionKeys;
        var aggregate = payload.Aggregates.TryGetValue(partitionKeys, out var existingAggregate)
            ? existingAggregate
            : Aggregate.EmptyFromPartitionKeys(partitionKeys);
        var projectedAggregate = aggregate.Project(ev, projector);
        return projectedAggregate.Match(
            success => success.GetPayload() is EmptyAggregatePayload
                ? ResultBox.FromValue(payload)
                : ResultBox.FromValue(
                    new AggregateListProjector<TAggregateProjector>(
                        payload.Aggregates.SetItem(partitionKeys, success))),
            ResultBox<AggregateListProjector<TAggregateProjector>>.Error);
    }
    public static string GetMultiProjectorName() => typeof(AggregateListProjector<TAggregateProjector>).Name +
        "+" +
        typeof(TAggregateProjector).Name;
}
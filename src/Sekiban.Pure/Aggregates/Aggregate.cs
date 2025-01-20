using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exception;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Aggregates;

public record Aggregate(
    IAggregatePayload Payload,
    PartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId) : IAggregate
{
    public static Aggregate Empty => new(new EmptyAggregatePayload(), PartitionKeys.Generate(), 0, string.Empty);
    public IAggregatePayload GetPayload() => Payload;
    public static Aggregate EmptyFromPartitionKeys(PartitionKeys keys) =>
        new(new EmptyAggregatePayload(), keys, 0, string.Empty);
    public ResultBox<Aggregate<TAggregatePayload>> ToTypedPayload<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload => Payload is TAggregatePayload typedPayload
        ? ResultBox.FromValue(
            new Aggregate<TAggregatePayload>(typedPayload, PartitionKeys, Version, LastSortableUniqueId))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
    public ResultBox<Aggregate> Project(IEvent ev, IAggregateProjector projector) => this with
    {
        Payload = projector.Project(Payload, ev),
        LastSortableUniqueId = ev.SortableUniqueId,
        Version = Version + 1
    };
    public ResultBox<Aggregate> Project(List<IEvent> events, IAggregateProjector projector) => ResultBox
        .FromValue(events)
        .ReduceEach(this, (ev, aggregate) => aggregate.Project(ev, projector));
}
public record Aggregate<TAggregatePayload>(
    TAggregatePayload Payload,
    PartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId) : IAggregate where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload() => Payload;
    public ResultBox<Aggregate<TAggregatePayload1>> ToTypedPayload<TAggregatePayload1>() where TAggregatePayload1 : IAggregatePayload => Payload is TAggregatePayload1 typedPayload
        ? ResultBox.FromValue(
            new Aggregate<TAggregatePayload1>(typedPayload, PartitionKeys, Version, LastSortableUniqueId))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
}

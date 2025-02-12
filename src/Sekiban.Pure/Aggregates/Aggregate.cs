using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Aggregates;

public record Aggregate(
    IAggregatePayload Payload,
    PartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId,
    string ProjectorVersion,
    string ProjectorTypeName,
    string PayloadTypeName) : IAggregate
{
    public static Aggregate Empty => new(new EmptyAggregatePayload(), PartitionKeys.Generate(), 0, string.Empty, string.Empty, string.Empty, nameof(EmptyAggregatePayload));
    public static Aggregate FromPayload(IAggregatePayload payload, PartitionKeys partitionKeys, int version, string LastSortableUniqueId, IAggregateProjector projector) =>
        new(payload, partitionKeys, version, LastSortableUniqueId, projector.GetVersion(), projector.GetType().Name, payload.GetType().Name);
    public IAggregatePayload GetPayload() => Payload;
    public static Aggregate EmptyFromPartitionKeys(PartitionKeys keys) =>
        new(new EmptyAggregatePayload(), keys, 0, string.Empty, String.Empty, String.Empty, nameof(EmptyAggregatePayload));
    public ResultBox<Aggregate<TAggregatePayload>> ToTypedPayload<TAggregatePayload>()
        where TAggregatePayload : IAggregatePayload => Payload is TAggregatePayload typedPayload
        ? ResultBox.FromValue(
            new Aggregate<TAggregatePayload>(typedPayload, PartitionKeys, Version, LastSortableUniqueId, ProjectorVersion, ProjectorTypeName, PayloadTypeName))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
    public ResultBox<Aggregate> Project(IEvent ev, IAggregateProjector projector) => 
        ResultBox.FromValue(projector.Project(Payload, ev)).Remap(projected => this with 
    {
        Payload = projected,
        LastSortableUniqueId = ev.SortableUniqueId,
        Version = Version + 1,
        ProjectorVersion = projector.GetVersion(),
        ProjectorTypeName = projector.GetType().Name,
        PayloadTypeName = projected.GetType().Name
    });
    public ResultBox<Aggregate> Project(List<IEvent> events, IAggregateProjector projector) => ResultBox
        .FromValue(events)
        .ReduceEach(this, (ev, aggregate) => aggregate.Project(ev, projector));
}
public record Aggregate<TAggregatePayload>(
    TAggregatePayload Payload,
    PartitionKeys PartitionKeys,
    int Version,
    string LastSortableUniqueId,string ProjectorVersion,
    string ProjectorTypeName,
    string PayloadTypeName) : IAggregate where TAggregatePayload : IAggregatePayload
{
    public IAggregatePayload GetPayload() => Payload;
    public ResultBox<Aggregate<TAggregatePayload1>> ToTypedPayload<TAggregatePayload1>() where TAggregatePayload1 : IAggregatePayload => Payload is TAggregatePayload1 typedPayload
        ? ResultBox.FromValue(
            new Aggregate<TAggregatePayload1>(typedPayload, PartitionKeys, Version, LastSortableUniqueId, ProjectorVersion, ProjectorTypeName, PayloadTypeName))
        : new SekibanAggregateTypeException("Payload is not typed to " + typeof(TAggregatePayload).Name);
}

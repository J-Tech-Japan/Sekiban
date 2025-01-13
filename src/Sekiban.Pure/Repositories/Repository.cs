using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Sekiban.Pure.Repositories;

public class Repository
{
    public static List<IEvent> Events { get; set; } = new();
    public Func<string, IEvent> Deserializer { get; set; } = s => throw new NotImplementedException();
    public Func<IEvent, string> Serializer { get; set; } = s => throw new NotImplementedException();
    public static ResultBox<Aggregate> Load<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() => Load(partitionKeys, new TAggregateProjector());
    public static ResultBox<Aggregate> Load(PartitionKeys partitionKeys, IAggregateProjector projector) =>
        ResultBox
            .FromValue(
                Events.Where(e => e.PartitionKeys.Equals(partitionKeys)).OrderBy(e => e.SortableUniqueId).ToList())
            .Conveyor(events => Aggregate.EmptyFromPartitionKeys(partitionKeys).Project(events, projector));

    public static ResultBox<UnitValue> Save(List<IEvent> events) => ResultBox.Start.Do(() => Events.AddRange(events));

    public static ResultBox<MultiProjectionState<TMultiProjection>> LoadMultiProjection<TMultiProjection>(
        IMultiProjectionEventSelector eventSelector) where TMultiProjection : IMultiProjector<TMultiProjection> =>
        ResultBox
            .FromValue(Events.Where(eventSelector.GetEventSelector).OrderBy(e => e.SortableUniqueId).ToList())
            .Conveyor(
                events => events
                    .ToResultBox()
                    .ReduceEach(new MultiProjectionState<TMultiProjection>(), (ev, state) => state.ApplyEvent(ev)));
}

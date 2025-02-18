using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Orleans.Parts;

public class OrleansRepository(
    IAggregateEventHandlerGrain eventHandlerGrain,
    PartitionKeys partitionKeys,
    IAggregateProjector projector,
    IEventTypes eventTypes,
    Aggregate aggregate)
{
    public Task<ResultBox<Aggregate>> Load()
        => ResultBox
            .FromValue(eventHandlerGrain.GetAllEventsAsync())
            .Remap(events => events.ToList())
            .Conveyor(events => aggregate.Project(events, projector));

    public Task<ResultBox<Aggregate>> GetAggregate()
        => aggregate.ToResultBox().ToTask();

    public Task<ResultBox<List<IEvent>>> Save(string lastSortableUniqueId, List<IEvent> events)
        => ResultBox
            .WrapTry(() => eventHandlerGrain.AppendEventsAsync(lastSortableUniqueId, events))
            .Conveyor(savedEvents => savedEvents.ToList().ToResultBox());

    public ResultBox<Aggregate> GetProjectedAggregate(List<IEvent> events)
        => aggregate.Project(events, projector);
}
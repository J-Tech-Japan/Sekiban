using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.DateProducer;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Command.Executor;

public class CommandContext<TAggregatePayload>(
    Aggregate aggregate,
    IAggregateProjector projector,
    IEventTypes eventTypes,
    CommandMetadata metadata,
    IServiceProvider serviceProvider) : ICommandContext<TAggregatePayload> where TAggregatePayload : IAggregatePayload
{
    public Aggregate Aggregate { get; set; } = aggregate;
    public IAggregateProjector Projector { get; } = projector;
    public IEventTypes EventTypes { get; } = eventTypes;
    public CommandMetadata CommandMetadata { get; } = metadata;
    public ResultBox<T> GetService<T>() where T : notnull => ResultBox.CheckNull(serviceProvider.GetService<T>());
    public EventMetadata EventMetadata { get; init; } = EventMetadata.FromCommandMetadata(metadata);
    public string OriginalSortableUniqueId { get; init; } = aggregate.LastSortableUniqueId;
    public List<IEvent> Events { get; } = new();
    public int GetNextVersion() => Aggregate.Version + 1;
    public int GetCurrentVersion() => Aggregate.Version;
    public IAggregate GetAggregateCommon() => Aggregate;
    public ResultBox<Aggregate<TAggregatePayload>> GetAggregate() => Aggregate.ToTypedPayload<TAggregatePayload>();
    public ResultBox<EventOrNone> AppendEvent(IEventPayload eventPayload)
    {
        var toAdd = EventTypes.GenerateTypedEvent(
            eventPayload,
            Aggregate.PartitionKeys,
            SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
            Aggregate.Version + 1,
            EventMetadata);
        if (!toAdd.IsSuccess) { return EventOrNone.Empty; }
        var ev = toAdd.GetValue();
        var aggregatePayload = Projector.Project(Aggregate.GetPayload(), toAdd.GetValue());
        var projected = Aggregate.Project(ev, Projector);
        if (projected.IsSuccess) { Aggregate = projected.GetValue(); } else { return EventOrNone.Empty; }
        Events.Add(ev);
        return EventOrNone.Empty;
    }
    public PartitionKeys GetPartitionKeys() => Aggregate.PartitionKeys;
}

using ResultBoxes;
using Sekiban.Core.Shared;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Executor;

public record CommandContextWithoutState(PartitionKeys PartitionKeys, IEventTypes EventTypes)
    : ICommandContextWithoutState
{
    public string OriginalSortableUniqueId => String.Empty;
    public List<IEvent> Events { get; } = new();
    public PartitionKeys GetPartitionKeys() => PartitionKeys;
    public int GetNextVersion() => 0;
    public int GetCurrentVersion() => 0;
    public CommandExecuted GetCommandExecuted(List<IEvent> producedEvents) => new(
        PartitionKeys,
        SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
        producedEvents);
    public ResultBox<EventOrNone> AppendEvent(IEventPayload eventPayload) => EventTypes
        .GenerateTypedEvent(
            eventPayload,
            PartitionKeys,
            SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
            0)
        .Do(ev => Events.Add(ev))
        .Remap(_ => EventOrNone.Empty);
}

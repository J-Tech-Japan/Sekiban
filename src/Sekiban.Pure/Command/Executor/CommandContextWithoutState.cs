using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Shared;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Executor;

public record CommandContextWithoutState(
    PartitionKeys PartitionKeys,
    IEventTypes EventTypes,
    CommandMetadata CommandMetadata,
    IServiceProvider ServiceProvider)
    : ICommandContextWithoutState
{
    public string OriginalSortableUniqueId => string.Empty;
    public List<IEvent> Events { get; } = new();
    public PartitionKeys GetPartitionKeys() => PartitionKeys;
    public int GetNextVersion() => 0;
    public int GetCurrentVersion() => 0;
    public EventMetadata EventMetadata { get; init; } = EventMetadata.FromCommandMetadata(CommandMetadata);
    public ResultBox<T> GetService<T>() where T : notnull => ResultBox.CheckNull(ServiceProvider.GetService<T>());

    public CommandExecuted GetCommandExecuted(List<IEvent> producedEvents) => new(
        PartitionKeys,
        SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
        producedEvents);
    public ResultBox<EventOrNone> AppendEvent(IEventPayload eventPayload) => EventTypes
        .GenerateTypedEvent(
            eventPayload,
            PartitionKeys,
            SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.NewGuid()),
            0,
            EventMetadata)
        .Do(ev => Events.Add(ev))
        .Remap(_ => EventOrNone.Empty);
}
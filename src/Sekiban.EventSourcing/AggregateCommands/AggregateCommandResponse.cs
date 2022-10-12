namespace Sekiban.EventSourcing.AggregateCommands;

public record AggregateCommandResponse(Guid AggregateId, ReadOnlyCollection<IAggregateEvent> Events, int Version)
{
}

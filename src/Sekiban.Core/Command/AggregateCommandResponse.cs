using Sekiban.Core.Event;
namespace Sekiban.Core.Command;

public record AggregateCommandResponse(Guid AggregateId, ReadOnlyCollection<IAggregateEvent> Events, int Version)
{
}

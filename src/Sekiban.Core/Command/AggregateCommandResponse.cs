using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public record AggregateCommandResponse(Guid AggregateId, ImmutableList<IAggregateEvent> Events, int Version)
{
}

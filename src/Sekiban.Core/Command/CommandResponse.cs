using Sekiban.Core.Event;
using System.Collections.Immutable;
namespace Sekiban.Core.Command;

public record CommandResponse(Guid AggregateId, ImmutableList<IEvent> Events, int Version)
{
}

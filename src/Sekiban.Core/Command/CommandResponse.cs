using System.Collections.Immutable;
using Sekiban.Core.Event;

namespace Sekiban.Core.Command;

public record CommandResponse(Guid AggregateId, ImmutableList<IEvent> Events, int Version)
{
}

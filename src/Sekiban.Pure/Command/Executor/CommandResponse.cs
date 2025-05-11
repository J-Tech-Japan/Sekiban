using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.Linq;

namespace Sekiban.Pure.Command.Executor;

public record CommandResponse(PartitionKeys PartitionKeys, List<IEvent> Events, int Version)
{
    public string? GetLastSortableUniqueId() => Events.LastOrDefault()?.SortableUniqueId;
}

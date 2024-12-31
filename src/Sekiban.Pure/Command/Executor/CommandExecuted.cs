using Sekiban.Pure.Events;
namespace Sekiban.Pure;

public record CommandExecuted(PartitionKeys PartitionKeys, string LastSortableUniqueId, List<IEvent> ProducedEvents);

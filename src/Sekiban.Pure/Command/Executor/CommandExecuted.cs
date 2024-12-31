using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Executor;

public record CommandExecuted(PartitionKeys PartitionKeys, string LastSortableUniqueId, List<IEvent> ProducedEvents);

using Sekiban.Pure.Events;
namespace Sekiban.Pure;

public record CommandResponse(PartitionKeys PartitionKeys, List<IEvent> Events, int Version);

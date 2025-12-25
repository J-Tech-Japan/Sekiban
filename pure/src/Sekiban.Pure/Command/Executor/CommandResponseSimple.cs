using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Command.Executor;

public record CommandResponseSimple(
    PartitionKeys PartitionKeys,
    string? LastSortableUniqueId,
    int NumberOfEvents,
    string? LastEventTypeName,
    int Version);

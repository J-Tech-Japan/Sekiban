using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using System.Linq;

namespace Sekiban.Pure.Command.Executor;

public record CommandResponse(PartitionKeys PartitionKeys, List<IEvent> Events, int Version)
{
    public string? GetLastSortableUniqueId() => Events.LastOrDefault()?.SortableUniqueId;
}

public record CommandResponseSimple(
    PartitionKeys PartitionKeys,
    string? LastSortableUniqueId,
    int NumberOfEvents,
    string? LastEventTypeName,
    int Version);

public static class CommandResponseExtension
{
    public static Task<ResultBox<CommandResponseSimple>> ToSimpleCommandResponse(
        this Task<ResultBox<CommandResponse>> taskResponse) => taskResponse.Remap(response => new CommandResponseSimple(response.PartitionKeys, response.Events?.LastOrDefault()?.SortableUniqueId, response.Events?.Count() ?? 0, response.Events?.LastOrDefault()?.GetPayload().GetType().Name, response.Version) );
}
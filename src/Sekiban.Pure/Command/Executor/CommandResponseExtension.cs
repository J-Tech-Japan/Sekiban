using ResultBoxes;
namespace Sekiban.Pure.Command.Executor;

public static class CommandResponseExtension
{
    public static Task<ResultBox<CommandResponseSimple>> ToSimpleCommandResponse(
        this Task<ResultBox<CommandResponse>> taskResponse) => taskResponse.Remap(ToSimpleCommandResponse);

    public static ResultBox<CommandResponseSimple> ToSimpleCommandResponse(
        this ResultBox<CommandResponse> taskResponse) =>
        taskResponse.Remap(ToSimpleCommandResponse);

    public static CommandResponseSimple ToSimpleCommandResponse(this CommandResponse response) => new(
        response.PartitionKeys,
        response.Events?.LastOrDefault()?.SortableUniqueId,
        response.Events?.Count() ?? 0,
        response.Events?.LastOrDefault()?.GetPayload().GetType().Name,
        response.Version);
}

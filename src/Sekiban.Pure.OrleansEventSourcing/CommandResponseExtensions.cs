using Sekiban.Pure.Command.Executor;

namespace Sekiban.Pure.OrleansEventSourcing;

public static class CommandResponseExtensions
{
    public static OrleansCommandResponse ToOrleansCommandResponse(this CommandResponse response) =>
        new(response.PartitionKeys.ToOrleansPartitionKeys(), response.Events.Select(e => e.ToString() ?? String.Empty).ToList(), response.Version);
}
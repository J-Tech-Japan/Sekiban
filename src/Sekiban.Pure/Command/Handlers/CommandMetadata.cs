using Sekiban.Pure.Extensions;
namespace Sekiban.Pure.Command.Handlers;

public record CommandMetadata(Guid CommandId, string CausationId, string CorrelationId, string ExecutedUser)
{
    public static CommandMetadata Create(string executedUser)
    {
        var commandId = GuidExtensions.CreateVersion7();
        return new CommandMetadata(commandId, string.Empty, commandId.ToString(), executedUser);
    }
}

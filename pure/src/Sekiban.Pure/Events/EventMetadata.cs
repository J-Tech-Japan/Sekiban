using Sekiban.Pure.Command.Handlers;
namespace Sekiban.Pure.Events;

public record EventMetadata(string CausationId, string CorrelationId, string ExecutedUser)
{
    public static EventMetadata FromCommandMetadata(CommandMetadata metadata) =>
        new(
            string.IsNullOrWhiteSpace(metadata.CausationId) ? metadata.CommandId.ToString() : metadata.CausationId,
            metadata.CorrelationId,
            metadata.ExecutedUser);
}

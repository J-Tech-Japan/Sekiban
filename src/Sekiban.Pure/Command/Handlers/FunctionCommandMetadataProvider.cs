using Sekiban.Pure.Events;
using Sekiban.Pure.Extensions;
namespace Sekiban.Pure.Command.Handlers;

public record FunctionCommandMetadataProvider(Func<string> GetExecutingUser) : ICommandMetadataProvider
{
    public CommandMetadata GetMetadata()
    {
        var commandId = GuidExtensions.CreateVersion7();
        return new CommandMetadata(commandId, "", commandId.ToString(), GetExecutingUser());
    }

    public CommandMetadata GetMetadataWithSubscribedEvent(IEvent ev) => new(
        GuidExtensions.CreateVersion7(),
        ev.Id.ToString(),
        ev.Metadata.CorrelationId,
        ev.Metadata.ExecutedUser);
}
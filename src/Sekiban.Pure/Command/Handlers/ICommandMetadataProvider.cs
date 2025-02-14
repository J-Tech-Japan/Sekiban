using Sekiban.Pure.Events;
namespace Sekiban.Pure.Command.Handlers;

public interface ICommandMetadataProvider
{
    CommandMetadata GetMetadata();
    CommandMetadata GetMetadataWithSubscribedEvent(IEvent ev);
}
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace Sekiban.Core.Documents;

public class EventWriter(
    IEventPersistentWriter writer,
    IEventTemporaryWriter temporaryWriter,
    EventPublisher eventPublisher)
{
    public async Task SaveEventsWithoutPublish<TEvent>(
        IEnumerable<TEvent> events,
        IWriteDocumentStream writeDocumentStream) where TEvent : IEvent
    {
        switch (writeDocumentStream.GetAggregateContainerGroup())
        {
            case AggregateContainerGroup.InMemory:
                await temporaryWriter.SaveEvents(events, writeDocumentStream);
                break;
            default:
                await writer.SaveEvents(events, writeDocumentStream);
                break;
        }
    }
    public Task SaveEvents<TEvent>(
        IEnumerable<TEvent> events,
        IWriteDocumentStream writeDocumentStream,
        bool withPublish) where TEvent : IEvent => withPublish
        ? SaveAndPublishEvents(events, writeDocumentStream)
        : SaveEventsWithoutPublish(events, writeDocumentStream);
    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent
    {
        var list = events.ToList();
        await SaveEventsWithoutPublish(list, writeDocumentStream);
        await eventPublisher.PublishEventsAsync(list);
    }
}

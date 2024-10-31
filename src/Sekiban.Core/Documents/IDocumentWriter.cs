using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
namespace Sekiban.Core.Documents;

/// <summary>
///     Document Writer interface
/// </summary>
public interface IDocumentWriter
{
    /// <summary>
    ///     Save document
    /// </summary>
    /// <param name="document"></param>
    /// <param name="aggregateType"></param>
    /// <typeparam name="TDocument"></typeparam>
    /// <returns></returns>
    Task SaveItemAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument;
}
public interface IEventWriter
{
    Task SaveEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream) where TEvent : IEvent;
}
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

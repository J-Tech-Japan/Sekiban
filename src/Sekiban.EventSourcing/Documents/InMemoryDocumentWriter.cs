using Sekiban.EventSourcing.PubSubs;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentWriter : IDocumentTemporaryWriter
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly AggregateEventPublisher _eventPublisher;

    public InMemoryDocumentWriter(InMemoryDocumentStore inMemoryDocumentStore, AggregateEventPublisher eventPublisher)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _eventPublisher = eventPublisher;
    }
    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : Document
    {
        if (document.DocumentType == DocumentType.AggregateEvent)
        {
            _inMemoryDocumentStore.SaveEvent((document as AggregateEvent)!, document.PartitionKey);
        }
        else
        {
            _inMemoryDocumentStore.SaveItem(document, document.PartitionKey);
        }
        await Task.CompletedTask;
    }
    public async Task SaveAndPublishAggregateEvent<TAggregateEvent>(
        TAggregateEvent aggregateEvent,
        Type aggregateType) where TAggregateEvent : AggregateEvent
    {
        _inMemoryDocumentStore.SaveEvent(aggregateEvent, aggregateEvent.PartitionKey);
        await _eventPublisher.PublishAsync(aggregateEvent);
    }
}

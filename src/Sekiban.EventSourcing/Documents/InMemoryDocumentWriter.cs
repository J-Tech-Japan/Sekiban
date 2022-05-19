using Microsoft.Extensions.Caching.Memory;
using Sekiban.EventSourcing.PubSubs;
using Sekiban.EventSourcing.Settings;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentWriter : IDocumentTemporaryWriter, IDocumentPersistentWriter
{
    private readonly AggregateEventPublisher _eventPublisher;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCache _memoryCache;
    private readonly string _sekibanIdentifier;
    public InMemoryDocumentWriter(
        InMemoryDocumentStore inMemoryDocumentStore,
        AggregateEventPublisher eventPublisher,
        IMemoryCache memoryCache,
        ISekibanContext sekibanContext)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _eventPublisher = eventPublisher;
        _memoryCache = memoryCache;
        _sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext.SettingGroupIdentifier) ? string.Empty : sekibanContext.SettingGroupIdentifier;
    }
    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : Document
    {
        switch (document.DocumentType)
        {
            case DocumentType.AggregateEvent:
                _inMemoryDocumentStore.SaveEvent((document as AggregateEvent)!, document.PartitionKey, _sekibanIdentifier);
                break;
            case DocumentType.AggregateSnapshot:
                if (document is SnapshotDocument sd)
                {
                    _memoryCache.Set(document.PartitionKey, sd);
                }
                break;
        }
        await Task.CompletedTask;
    }
    public async Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent, Type aggregateType)
        where TAggregateEvent : AggregateEvent
    {
        _inMemoryDocumentStore.SaveEvent(aggregateEvent, aggregateEvent.PartitionKey, _sekibanIdentifier);
        await _eventPublisher.PublishAsync(aggregateEvent);
    }
}

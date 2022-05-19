using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.PubSubs;
using Sekiban.EventSourcing.Settings;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentWriter : IDocumentTemporaryWriter, IDocumentPersistentWriter
{
    private readonly AggregateEventPublisher _eventPublisher;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCache _memoryCache;
    private readonly IServiceProvider _serviceProvider;
    public InMemoryDocumentWriter(
        InMemoryDocumentStore inMemoryDocumentStore,
        AggregateEventPublisher eventPublisher,
        IMemoryCache memoryCache,
        IServiceProvider serviceProvider)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _eventPublisher = eventPublisher;
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
    }
    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : Document
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        switch (document.DocumentType)
        {
            case DocumentType.AggregateEvent:
                _inMemoryDocumentStore.SaveEvent((document as AggregateEvent)!, document.PartitionKey, sekibanIdentifier);
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
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        _inMemoryDocumentStore.SaveEvent(aggregateEvent, aggregateEvent.PartitionKey, sekibanIdentifier);
        await _eventPublisher.PublishAsync(aggregateEvent);
    }
}

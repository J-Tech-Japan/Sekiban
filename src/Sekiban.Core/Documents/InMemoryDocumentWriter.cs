using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Event;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

public class InMemoryDocumentWriter : IDocumentTemporaryWriter, IDocumentPersistentWriter
{
    private readonly EventPublisher _eventPublisher;
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISnapshotDocumentCache _snapshotDocumentCache;
    public InMemoryDocumentWriter(
        InMemoryDocumentStore inMemoryDocumentStore,
        EventPublisher eventPublisher,
        IServiceProvider serviceProvider,
        ISnapshotDocumentCache snapshotDocumentCache)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _eventPublisher = eventPublisher;
        _serviceProvider = serviceProvider;
        _snapshotDocumentCache = snapshotDocumentCache;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        switch (document.DocumentType)
        {
            case DocumentType.Event:
                _inMemoryDocumentStore.SaveEvent((document as IEvent)!, document.PartitionKey, sekibanIdentifier);
                break;
            case DocumentType.AggregateSnapshot:
                if (document is SnapshotDocument sd)
                {
                    _snapshotDocumentCache.Set(sd);
                }
                break;
        }

        await Task.CompletedTask;
    }

    public async Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType)
        where TEvent : IEvent
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        _inMemoryDocumentStore.SaveEvent(ev, ev.PartitionKey, sekibanIdentifier);
        await _eventPublisher.PublishAsync(ev);
    }
}

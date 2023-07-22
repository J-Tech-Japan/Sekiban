using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Events;
using Sekiban.Core.PubSub;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

/// <summary>
///     In memory Document Writer
///     App developer does not need to use this class
///     see <see cref="IDocumentWriter" />
/// </summary>
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
    public Task SaveSingleSnapshotAsync(SnapshotDocument document, Type aggregateType, bool useBlob)
    {
        _snapshotDocumentCache.Set(document);
        return Task.CompletedTask;
    }
    public bool ShouldUseBlob(SnapshotDocument document) => false;

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
                    await SaveSingleSnapshotAsync(sd, aggregateType, ShouldUseBlob(sd));
                }
                break;
        }

        await Task.CompletedTask;
    }

    public async Task SaveAndPublishEvents<TEvent>(IEnumerable<TEvent> events, Type aggregateType) where TEvent : IEvent
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        foreach (var ev in events)
        {
            _inMemoryDocumentStore.SaveEvent(ev, ev.PartitionKey, sekibanIdentifier);
            await _eventPublisher.PublishAsync(ev);
        }
    }
}

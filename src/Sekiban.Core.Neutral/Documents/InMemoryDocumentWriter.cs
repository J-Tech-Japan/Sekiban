using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents.Pools;
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
public class InMemoryDocumentWriter : IDocumentTemporaryWriter,
    IDocumentPersistentWriter,
    IEventPersistentWriter,
    IEventTemporaryWriter
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
    public Task SaveSingleSnapshotAsync(
        SnapshotDocument document,
        IWriteDocumentStream writeDocumentStream,
        bool useBlob)
    {
        _snapshotDocumentCache.Set(document);
        return Task.CompletedTask;
    }
    public bool ShouldUseBlob(SnapshotDocument document) => false;

    public async Task SaveItemAsync<TDocument>(TDocument document, IWriteDocumentStream writeDocumentStream)
        where TDocument : IDocument
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
                    await SaveSingleSnapshotAsync(sd, writeDocumentStream, ShouldUseBlob(sd));
                }
                break;
        }

        await Task.CompletedTask;
    }
    public async Task SaveEvents<TEvent>(IEnumerable<TEvent> events, IWriteDocumentStream writeDocumentStream)
        where TEvent : IEvent
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        foreach (var ev in events)
        {
            _inMemoryDocumentStore.SaveEvent(ev, ev.PartitionKey, sekibanIdentifier);
        }
        await Task.CompletedTask;
    }
}

using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Partition;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Document;

public class DocumentWriterSplitter : IDocumentWriter
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentWriter _documentPersistentWriter;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    public DocumentWriterSplitter(
        IDocumentPersistentWriter documentPersistentWriter,
        IDocumentTemporaryWriter documentTemporaryWriter,
        HybridStoreManager hybridStoreManager,
        IAggregateSettings aggregateSettings)
    {
        _documentPersistentWriter = documentPersistentWriter;
        _documentTemporaryWriter = documentTemporaryWriter;
        _hybridStoreManager = hybridStoreManager;
        _aggregateSettings = aggregateSettings;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType) where TDocument : IDocument
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryWriter.SaveAsync(document, aggregateType);
            return;
        }
        if (document.DocumentType == DocumentType.AggregateSnapshot) { }
        if (document is IEvent) { }
        await _documentPersistentWriter.SaveAsync(document, aggregateType);
    }
    public async Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType)
        where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryWriter.SaveAndPublishEvent(ev, aggregateType);
            return;
        }
        await AddToHybridIfPossible(ev, aggregateType);
        await _documentPersistentWriter.SaveAndPublishEvent(ev, aggregateType);
    }

    private async Task AddToHybridIfPossible(IEvent @event, Type aggregateType)
    {
        if (!_aggregateSettings.CanUseHybrid(aggregateType)) { return; }
        if (@event.IsAggregateInitialEvent)
        {
            if (_hybridStoreManager.AddPartitionKey(@event.PartitionKey, string.Empty))
            {
                await _documentTemporaryWriter.SaveAsync(@event, aggregateType);
            }
        }
        else
        {
            if (_hybridStoreManager.HasPartition(@event.PartitionKey))
            {
                await _documentTemporaryWriter.SaveAsync(@event, aggregateType);
            }
        }
    }
    private async Task SaveSnapshotToHybridIfPossible(SnapshotDocument snapshot, Type aggregateType)
    {
        if (_hybridStoreManager.HasPartition(PartitionKeyGenerator.ForEvent(snapshot.AggregateId, aggregateType)))
        {
            await _documentTemporaryWriter.SaveAsync(snapshot, aggregateType);
        }
    }
}

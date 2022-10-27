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
        if (document is IAggregateEvent) { }
        await _documentPersistentWriter.SaveAsync(document, aggregateType);
    }
    public async Task SaveAndPublishAggregateEvent<TAggregateEvent>(TAggregateEvent aggregateEvent, Type aggregateType)
        where TAggregateEvent : IAggregateEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryWriter.SaveAndPublishAggregateEvent(aggregateEvent, aggregateType);
            return;
        }
        await AddToHybridIfPossible(aggregateEvent, aggregateType);
        await _documentPersistentWriter.SaveAndPublishAggregateEvent(aggregateEvent, aggregateType);
    }

    private async Task AddToHybridIfPossible(IAggregateEvent aggregateEvent, Type aggregateType)
    {
        if (!_aggregateSettings.CanUseHybrid(aggregateType)) { return; }
        if (aggregateEvent.IsAggregateInitialEvent)
        {
            if (_hybridStoreManager.AddPartitionKey(aggregateEvent.PartitionKey, string.Empty))
            {
                await _documentTemporaryWriter.SaveAsync(aggregateEvent, aggregateType);
            }
        }
        else
        {
            if (_hybridStoreManager.HasPartition(aggregateEvent.PartitionKey))
            {
                await _documentTemporaryWriter.SaveAsync(aggregateEvent, aggregateType);
            }
        }
    }
    private async Task SaveSnapshotToHybridIfPossible(SnapshotDocument snapshot, Type aggregateType)
    {
        if (_hybridStoreManager.HasPartition(PartitionKeyGenerator.ForAggregateEvent(snapshot.AggregateId, aggregateType)))
        {
            await _documentTemporaryWriter.SaveAsync(snapshot, aggregateType);
        }
    }
}

using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Partition;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

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
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryWriter.SaveAsync(document, aggregateType);
            return;
        }

        if (document.DocumentType == DocumentType.AggregateSnapshot)
        {
        }

        if (document is IEvent)
        {
        }

        await _documentPersistentWriter.SaveAsync(document, aggregateType);
    }

    public async Task SaveAndPublishEvent<TEvent>(TEvent ev, Type aggregateType)
        where TEvent : IEvent
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryWriter.SaveAndPublishEvent(ev, aggregateType);
            return;
        }

        await AddToHybridIfPossible(ev, aggregateType);
        await _documentPersistentWriter.SaveAndPublishEvent(ev, aggregateType);
    }

    private async Task AddToHybridIfPossible(IEvent ev, Type aggregateType)
    {
        if (!_aggregateSettings.CanUseHybrid(aggregateType))
        {
            return;
        }
        if (!_hybridStoreManager.HasPartition(ev.PartitionKey))
        {
            _hybridStoreManager.AddPartitionKey(ev.PartitionKey, string.Empty, false);
        }
        await _documentTemporaryWriter.SaveAsync(ev, aggregateType);
    }

    private async Task SaveSnapshotToHybridIfPossible(SnapshotDocument snapshot, Type aggregateType)
    {
        if (_hybridStoreManager.HasPartition(PartitionKeyGenerator.ForEvent(snapshot.AggregateId, aggregateType)))
        {
            await _documentTemporaryWriter.SaveAsync(snapshot, aggregateType);
        }
    }
}
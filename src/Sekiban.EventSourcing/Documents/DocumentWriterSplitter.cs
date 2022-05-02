namespace Sekiban.EventSourcing.Documents;

public class DocumentWriterSplitter : IDocumentWriter
{
    private readonly IDocumentPersistentWriter _documentPersistentWriter;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    public DocumentWriterSplitter(
        IDocumentPersistentWriter documentPersistentWriter,
        IDocumentTemporaryWriter documentTemporaryWriter,
        HybridStoreManager hybridStoreManager)
    {
        _documentPersistentWriter = documentPersistentWriter;
        _documentTemporaryWriter = documentTemporaryWriter;
        _hybridStoreManager = hybridStoreManager;
    }

    public async Task SaveAsync<TDocument>(TDocument document, Type aggregateType)
        where TDocument : Document
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryWriter.SaveAsync(document, aggregateType);
            return;
        }

        if (document is AggregateEvent) { }
        await _documentPersistentWriter.SaveAsync(document, aggregateType);
    }
    public async Task SaveAndPublishAggregateEvent<TAggregateEvent>(
        TAggregateEvent aggregateEvent,
        Type aggregateType) where TAggregateEvent : AggregateEvent
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryWriter.SaveAndPublishAggregateEvent(
                aggregateEvent,
                aggregateType);
            return;
        }
        await AddToHybridIfPossible(aggregateEvent, aggregateType);
        await _documentPersistentWriter.SaveAndPublishAggregateEvent(
            aggregateEvent,
            aggregateType);
    }

    private async Task AddToHybridIfPossible(AggregateEvent aggregateEvent, Type aggregateType)
    {
        if (aggregateEvent.IsAggregateInitialEvent)
        {
            _hybridStoreManager.HybridPartitionKeys.Add(aggregateEvent.PartitionKey);
            await _documentTemporaryWriter.SaveAndPublishAggregateEvent(
                aggregateEvent,
                aggregateType);
        }
        else
        {
            if (_hybridStoreManager.HybridPartitionKeys.Contains(aggregateEvent.PartitionKey))
            {
                await _documentTemporaryWriter.SaveAndPublishAggregateEvent(
                    aggregateEvent,
                    aggregateType);
            }
        }
    }
}

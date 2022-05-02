namespace Sekiban.EventSourcing.Documents;

public class DocumentRepositorySplitter : IDocumentRepository
{
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentTemporaryRepository _documentTemporaryRepository;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    public DocumentRepositorySplitter(
        IDocumentPersistentRepository documentPersistentRepository,
        IDocumentTemporaryRepository documentTemporaryRepository,
        HybridStoreManager hybridStoreManager, IDocumentTemporaryWriter documentTemporaryWriter)
    {
        _documentPersistentRepository = documentPersistentRepository;
        _documentTemporaryRepository = documentTemporaryRepository;
        _hybridStoreManager = hybridStoreManager;
        _documentTemporaryWriter = documentTemporaryWriter;
    }

    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        Guid? sinceEventId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
           await _documentTemporaryRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                originalType,
                partitionKey,
                sinceEventId,
                resultAction);
            return;
        }
        if (partitionKey != null && _hybridStoreManager.HasPartition(partitionKey))
        {
            await _documentTemporaryRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                originalType,
                partitionKey,
                sinceEventId,
                resultAction);
            return;
        }
        await _documentPersistentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            originalType,
            partitionKey,
            sinceEventId,
            events =>
            {
                if (sinceEventId == null && partitionKey!=null)
                {
                    _hybridStoreManager.AddPartitionKey(partitionKey);
                    foreach (AggregateEvent aggregateEvent in events)
                    {
                        _documentTemporaryWriter.SaveAsync(aggregateEvent, originalType).Wait();
                    }
                }
                resultAction(events);
            });
    }
    public Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        Guid? sinceEventId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
                originalType,
                sinceEventId,
                resultAction);
        }
        return _documentPersistentRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
            originalType,
            sinceEventId,
            events =>
            {
                resultAction(events);
            });
    }
    public Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                originalType,
                partitionKey);
        }
        return _documentPersistentRepository.GetLatestSnapshotForAggregateAsync(
            aggregateId,
            originalType,
            partitionKey);
    }
    public Task<SnapshotListDocument?> GetLatestSnapshotListForTypeAsync<T>(
        string? partitionKey,
        QueryListType queryListType = QueryListType.ActiveAndDeleted) where T : IAggregate
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetLatestSnapshotListForTypeAsync<T>(
                partitionKey,
                queryListType);
        }
        return _documentPersistentRepository.GetLatestSnapshotListForTypeAsync<T>(
            partitionKey,
            queryListType);
    }
    public Task<SnapshotListChunkDocument?> GetSnapshotListChunkByIdAsync(
        Guid id,
        string partitionKey) =>
        _documentPersistentRepository.GetSnapshotListChunkByIdAsync(id, partitionKey);
    public Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Type originalType,
        string partitionKey)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetSnapshotByIdAsync(
                id,
                originalType,
                partitionKey);
        }
        return _documentPersistentRepository.GetSnapshotByIdAsync(id, originalType, partitionKey);
    }
}

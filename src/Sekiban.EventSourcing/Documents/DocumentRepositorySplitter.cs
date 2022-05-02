namespace Sekiban.EventSourcing.Documents;

public class DocumentRepositorySplitter : IDocumentRepository
{
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentTemporaryRepository _documentTemporaryRepository;
    private readonly HybridStoreManager _hybridStoreManager;
    public DocumentRepositorySplitter(
        IDocumentPersistentRepository documentPersistentRepository,
        IDocumentTemporaryRepository documentTemporaryRepository,
        HybridStoreManager hybridStoreManager)
    {
        _documentPersistentRepository = documentPersistentRepository;
        _documentTemporaryRepository = documentTemporaryRepository;
        _hybridStoreManager = hybridStoreManager;
    }

    public Task GetAllAggregateEventsForAggregateIdAsync(
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
            return _documentTemporaryRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                originalType,
                partitionKey,
                sinceEventId,
                resultAction);
        }
        if (partitionKey != null && _hybridStoreManager.HybridPartitionKeys.Contains(partitionKey))
        {
            return _documentTemporaryRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                originalType,
                partitionKey,
                sinceEventId,
                resultAction);
        }
        return _documentPersistentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            originalType,
            partitionKey,
            sinceEventId,
            events =>
            {
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

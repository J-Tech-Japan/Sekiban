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
        HybridStoreManager hybridStoreManager,
        IDocumentTemporaryWriter documentTemporaryWriter)
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
        string? sinceSortableUniqueId,
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
                sinceSortableUniqueId,
                resultAction);
            return;
        }
        if (partitionKey != null &&
            _hybridStoreManager.HasPartition(partitionKey))
        {
            if ((string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    string.IsNullOrWhiteSpace(
                        _hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey))) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    await _documentTemporaryRepository
                        .AggregateEventsForAggregateIdHasSortableUniqueIdAsync(
                            aggregateId,
                            originalType,
                            partitionKey,
                            sinceSortableUniqueId)))
            {
                await _documentTemporaryRepository.GetAllAggregateEventsForAggregateIdAsync(
                    aggregateId,
                    originalType,
                    partitionKey,
                    sinceSortableUniqueId,
                    resultAction);
                return;
            }
        }
        await _documentPersistentRepository.GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            originalType,
            partitionKey,
            sinceSortableUniqueId,
            events =>
            {
                var aggregateEvents = events.ToList();
                if (partitionKey == null) { return; }
                var hasPartitionKey =
                    _hybridStoreManager.HasPartition(partitionKey);
                var sinceSortableUniqueIdInPartition =
                    _hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey);

                if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                {
                    SaveAggregateEvents(aggregateEvents, originalType, partitionKey, string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                {
                    if (!hasPartitionKey)
                    {
                        SaveAggregateEvents(
                            aggregateEvents,
                            originalType,
                            partitionKey,
                            sinceSortableUniqueId);
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueIdInPartition) &&
                            string.Compare(
                                sinceSortableUniqueIdInPartition!,
                                sinceSortableUniqueId!,
                                StringComparison.Ordinal) >
                            0)
                        {
                            SaveAggregateEvents(
                                aggregateEvents,
                                originalType,
                                partitionKey,
                                sinceSortableUniqueId);
                        }
                    }
                }
                Console.WriteLine($"{aggregateEvents.Count} events selected");
                resultAction(aggregateEvents.OrderBy(m => m.SortableUniqueId));
            });
    }
    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type originalType)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                originalType);
        }
        return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                originalType) ??
            await _documentPersistentRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                originalType);
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
    public Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
                originalType,
                sinceSortableUniqueId,
                resultAction);
        }
        return _documentPersistentRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
            originalType,
            sinceSortableUniqueId,
            events =>
            {
                resultAction(events);
            });
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type originalType,
        int version)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return false;
        }
        return
            await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                aggregateId,
                originalType,
                version);
    }
    private void SaveAggregateEvents(
        List<AggregateEvent> aggregateEvents,
        Type originalType,
        string partitionKey,
        string sortableUniqueKey)
    {
        _hybridStoreManager.AddPartitionKey(partitionKey, sortableUniqueKey);
        foreach (var aggregateEvent in aggregateEvents)
        {
            _documentTemporaryWriter.SaveAsync(aggregateEvent, originalType).Wait();
        }
    }
}

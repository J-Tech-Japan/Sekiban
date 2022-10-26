using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Document;

public class DocumentRepositorySplitter : IDocumentRepository
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentTemporaryRepository _documentTemporaryRepository;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    public DocumentRepositorySplitter(
        IDocumentPersistentRepository documentPersistentRepository,
        IDocumentTemporaryRepository documentTemporaryRepository,
        HybridStoreManager hybridStoreManager,
        IDocumentTemporaryWriter documentTemporaryWriter,
        IAggregateSettings aggregateSettings)
    {
        _documentPersistentRepository = documentPersistentRepository;
        _documentTemporaryRepository = documentTemporaryRepository;
        _hybridStoreManager = hybridStoreManager;
        _documentTemporaryWriter = documentTemporaryWriter;
        _aggregateSettings = aggregateSettings;
    }

    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
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
        if (partitionKey is not null && _aggregateSettings.CanUseHybrid(originalType) && _hybridStoreManager.HasPartition(partitionKey))
        {
            if ((string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    string.IsNullOrWhiteSpace(_hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey))) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    await _documentTemporaryRepository.AggregateEventsForAggregateIdHasSortableUniqueIdAsync(
                        aggregateId,
                        originalType,
                        partitionKey,
                        sinceSortableUniqueId)) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    sinceSortableUniqueId.Equals(_hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey))))
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
                if (_aggregateSettings.CanUseHybrid(originalType))
                {
                    if (partitionKey is null) { return; }
                    var hasPartitionKey = _hybridStoreManager.HasPartition(partitionKey);
                    var sinceSortableUniqueIdInPartition = _hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey);

                    if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        SaveAggregateEvents(aggregateEvents, originalType, partitionKey, string.Empty);
                    }

                    if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        if (!hasPartitionKey)
                        {
                            SaveAggregateEvents(aggregateEvents, originalType, partitionKey, sinceSortableUniqueId);
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(sinceSortableUniqueIdInPartition) &&
                                string.Compare(sinceSortableUniqueIdInPartition!, sinceSortableUniqueId!, StringComparison.Ordinal) > 0)
                            {
                                SaveAggregateEvents(aggregateEvents, originalType, partitionKey, sinceSortableUniqueId);
                            }
                        }
                    }
                }
                resultAction(aggregateEvents.OrderBy(m => m.SortableUniqueId));
            });
    }
    public async Task GetAllAggregateEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction)
    {
        await GetAllAggregateEventsForAggregateIdAsync(
            aggregateId,
            originalType,
            partitionKey,
            sinceSortableUniqueId,
            events =>
            {
                resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
            });
    }

    public async Task GetAllAggregateCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryRepository.GetAllAggregateCommandStringsForAggregateIdAsync(
                aggregateId,
                originalType,
                sinceSortableUniqueId,
                resultAction);
            return;
        }
        await _documentPersistentRepository.GetAllAggregateCommandStringsForAggregateIdAsync(
            aggregateId,
            originalType,
            sinceSortableUniqueId,
            resultAction);
    }

    public async Task GetAllAggregateEventsAsync(
        Type multipleProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multipleProjectionType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryRepository.GetAllAggregateEventsAsync(
                multipleProjectionType,
                targetAggregateNames,
                sinceSortableUniqueId,
                resultAction);
            return;
        }
        await _documentPersistentRepository.GetAllAggregateEventsAsync(
            multipleProjectionType,
            targetAggregateNames,
            sinceSortableUniqueId,
            resultAction);
    }
    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(aggregateId, originalType);
        }
        return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(aggregateId, originalType) ??
            await _documentPersistentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, originalType);
    }
    public Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type originalType, string partitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetSnapshotByIdAsync(id, originalType, partitionKey);
        }
        return _documentPersistentRepository.GetSnapshotByIdAsync(id, originalType, partitionKey);
    }
    public Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetAllAggregateEventsForAggregateEventTypeAsync(originalType, sinceSortableUniqueId, resultAction);
        }
        return _documentPersistentRepository.GetAllAggregateEventsForAggregateEventTypeAsync(
            originalType,
            sinceSortableUniqueId,
            events =>
            {
                resultAction(events);
            });
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return false;
        }
        return await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(aggregateId, originalType, version);
    }
    private void SaveAggregateEvents(List<IAggregateEvent> aggregateEvents, Type originalType, string partitionKey, string sortableUniqueKey)
    {
        _hybridStoreManager.AddPartitionKey(partitionKey, sortableUniqueKey);
        foreach (var aggregateEvent in aggregateEvents)
        {
            _documentTemporaryWriter.SaveAsync(aggregateEvent, originalType).Wait();
        }
    }
}

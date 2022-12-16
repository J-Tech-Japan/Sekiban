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

    public async Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryRepository.GetAllEventsForAggregateIdAsync(
                aggregateId,
                originalType,
                partitionKey,
                sinceSortableUniqueId,
                resultAction);
            return;
        }

        if (partitionKey is not null &&
            _aggregateSettings.CanUseHybrid(originalType) &&
            _hybridStoreManager.HasPartition(partitionKey))
        {
            if ((string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    string.IsNullOrWhiteSpace(_hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey))) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    await _documentTemporaryRepository.EventsForAggregateIdHasSortableUniqueIdAsync(
                        aggregateId,
                        originalType,
                        partitionKey,
                        sinceSortableUniqueId)) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    sinceSortableUniqueId.Equals(_hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey))))
            {
                await _documentTemporaryRepository.GetAllEventsForAggregateIdAsync(
                    aggregateId,
                    originalType,
                    partitionKey,
                    sinceSortableUniqueId,
                    resultAction);
                return;
            }
        }

        await _documentPersistentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            originalType,
            partitionKey,
            sinceSortableUniqueId,
            events =>
            {
                var eventList = events.ToList();
                if (_aggregateSettings.CanUseHybrid(originalType))
                {
                    if (partitionKey is null)
                    {
                        return;
                    }
                    var hasPartitionKey = _hybridStoreManager.HasPartition(partitionKey);
                    var sinceSortableUniqueIdInPartition =
                        _hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey);

                    if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        SaveEvents(eventList, originalType, partitionKey, string.Empty);
                    }

                    if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        if (!hasPartitionKey)
                        {
                            SaveEvents(eventList, originalType, partitionKey, sinceSortableUniqueId);
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
                                SaveEvents(eventList, originalType, partitionKey, sinceSortableUniqueId);
                            }
                        }
                    }
                }

                resultAction(eventList.OrderBy(m => m.SortableUniqueId));
            });
    }

    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction)
    {
        await GetAllEventsForAggregateIdAsync(
            aggregateId,
            originalType,
            partitionKey,
            sinceSortableUniqueId,
            events =>
            {
                resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
            });
    }

    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryRepository.GetAllCommandStringsForAggregateIdAsync(
                aggregateId,
                originalType,
                sinceSortableUniqueId,
                resultAction);
            return;
        }

        await _documentPersistentRepository.GetAllCommandStringsForAggregateIdAsync(
            aggregateId,
            originalType,
            sinceSortableUniqueId,
            resultAction);
    }

    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            await _documentTemporaryRepository.GetAllEventsAsync(
                multiProjectionType,
                targetAggregateNames,
                sinceSortableUniqueId,
                resultAction);
            return;
        }

        await _documentPersistentRepository.GetAllEventsAsync(
            multiProjectionType,
            targetAggregateNames,
            sinceSortableUniqueId,
            resultAction);
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType, string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(aggregateId, originalType, payloadVersionIdentifier);
        }
        return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(aggregateId, originalType, payloadVersionIdentifier) ??
            await _documentPersistentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, originalType, payloadVersionIdentifier);
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

    public Task GetAllEventsForAggregateAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetAllEventsForAggregateAsync(
                originalType,
                sinceSortableUniqueId,
                resultAction);
        }
        return _documentPersistentRepository.GetAllEventsForAggregateAsync(
            originalType,
            sinceSortableUniqueId,
            events => { resultAction(events); });
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version, string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return false;
        }
        return await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(aggregateId, originalType, version, payloadVersionIdentifier);
    }

    private void SaveEvents(List<IEvent> events, Type originalType, string partitionKey, string sortableUniqueKey)
    {
        _hybridStoreManager.AddPartitionKey(partitionKey, sortableUniqueKey);
        foreach (var ev in events)
        {
            _documentTemporaryWriter.SaveAsync(ev, originalType).Wait();
        }
    }
}

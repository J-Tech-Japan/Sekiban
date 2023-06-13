using Sekiban.Core.Aggregate;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents;

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
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        if (!aggregatePayloadType.IsAggregatePayloadType())
        {
            throw new SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException(
                aggregatePayloadType.FullName + "is not aggregate payload");
        }
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryRepository.GetAllEventsForAggregateIdAsync(
                aggregateId,
                aggregatePayloadType,
                partitionKey,
                sinceSortableUniqueId,
                rootPartitionKey,
                resultAction);
            return;
        }

        if (partitionKey is not null && _aggregateSettings.CanUseHybrid(aggregatePayloadType) && _hybridStoreManager.HasPartition(partitionKey))
        {
            if ((string.IsNullOrWhiteSpace(sinceSortableUniqueId) && _hybridStoreManager.FromInitialForPartitionKey(partitionKey)) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    await _documentTemporaryRepository.EventsForAggregateIdHasSortableUniqueIdAsync(
                        aggregateId,
                        aggregatePayloadType,
                        partitionKey,
                        sinceSortableUniqueId)) ||
                (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                    sinceSortableUniqueId.Equals(_hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey))))
            {
                await _documentTemporaryRepository.GetAllEventsForAggregateIdAsync(
                    aggregateId,
                    aggregatePayloadType,
                    partitionKey,
                    sinceSortableUniqueId,
                    rootPartitionKey,
                    resultAction);
                return;
            }
        }
        await _documentPersistentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            aggregatePayloadType,
            partitionKey,
            sinceSortableUniqueId,
            rootPartitionKey,
            events =>
            {
                var eventList = events.ToList();
                if (_aggregateSettings.CanUseHybrid(aggregatePayloadType))
                {
                    if (partitionKey is null)
                    {
                        return;
                    }
                    var hasPartitionKey = _hybridStoreManager.HasPartition(partitionKey);
                    var sinceSortableUniqueIdInPartition = _hybridStoreManager.SortableUniqueIdForPartitionKey(partitionKey);
                    var fromInitial = _hybridStoreManager.FromInitialForPartitionKey(partitionKey);

                    if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        SaveEvents(eventList, aggregatePayloadType, partitionKey, string.Empty, true);
                    }

                    if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        if (!hasPartitionKey)
                        {
                            SaveEvents(eventList, aggregatePayloadType, partitionKey, sinceSortableUniqueId, false);
                        } else
                        {
                            if ((!string.IsNullOrWhiteSpace(sinceSortableUniqueIdInPartition) || !fromInitial) &&
                                string.Compare(sinceSortableUniqueIdInPartition!, sinceSortableUniqueId!, StringComparison.Ordinal) > 0)
                            {
                                SaveEvents(eventList, aggregatePayloadType, partitionKey, sinceSortableUniqueId, false);
                            }
                        }
                    }
                }

                resultAction(eventList.OrderBy(m => m.SortableUniqueId));
            });
    }

    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        await GetAllEventsForAggregateIdAsync(
            aggregateId,
            aggregatePayloadType,
            partitionKey,
            sinceSortableUniqueId,
            rootPartitionKey,
            events =>
            {
                resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
            });
    }

    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        if (!aggregatePayloadType.IsAggregatePayloadType())
        {
            throw new SekibanCanNotRetrieveEventBecauseOriginalTypeIsNotAggregatePayloadException(
                aggregatePayloadType.FullName + "is not aggregate payload");
        }
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryRepository.GetAllCommandStringsForAggregateIdAsync(
                aggregateId,
                aggregatePayloadType,
                sinceSortableUniqueId,
                rootPartitionKey,
                resultAction);
            return;
        }

        await _documentPersistentRepository.GetAllCommandStringsForAggregateIdAsync(
            aggregateId,
            aggregatePayloadType,
            sinceSortableUniqueId,
            rootPartitionKey,
            resultAction);
    }

    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            await _documentTemporaryRepository.GetAllEventsAsync(
                multiProjectionType,
                targetAggregateNames,
                sinceSortableUniqueId,
                rootPartitionKey,
                resultAction);
            return;
        }

        await _documentPersistentRepository.GetAllEventsAsync(
            multiProjectionType,
            targetAggregateNames,
            sinceSortableUniqueId,
            rootPartitionKey,
            resultAction);
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                rootPartitionKey,
                payloadVersionIdentifier);
        }
        return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                rootPartitionKey,
                payloadVersionIdentifier) ??
            await _documentPersistentRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                rootPartitionKey,
                payloadVersionIdentifier);
    }
    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return await _documentTemporaryRepository.GetLatestSnapshotForMultiProjectionAsync(
                multiProjectionPayloadType,
                payloadVersionIdentifier,
                rootPartitionKey);
        }
        return await _documentTemporaryRepository.GetLatestSnapshotForMultiProjectionAsync(
                multiProjectionPayloadType,
                payloadVersionIdentifier,
                rootPartitionKey) ??
            await _documentPersistentRepository.GetLatestSnapshotForMultiProjectionAsync(
                multiProjectionPayloadType,
                payloadVersionIdentifier,
                rootPartitionKey);
    }

    public Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string partitionKey,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return _documentTemporaryRepository.GetSnapshotByIdAsync(
                id,
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                partitionKey,
                rootPartitionKey);
        }
        return _documentPersistentRepository.GetSnapshotByIdAsync(
            id,
            aggregateId,
            aggregatePayloadType,
            projectionPayloadType,
            partitionKey,
            rootPartitionKey);
    }

    public Task GetAllEventsForAggregateAsync(Type aggregatePayloadType, string? sinceSortableUniqueId, Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return _documentTemporaryRepository.GetAllEventsForAggregateAsync(aggregatePayloadType, sinceSortableUniqueId, resultAction);
        }
        return _documentPersistentRepository.GetAllEventsForAggregateAsync(
            aggregatePayloadType,
            sinceSortableUniqueId,
            events => { resultAction(events); });
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return false;
        }
        return await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
            aggregateId,
            aggregatePayloadType,
            projectionPayloadType,
            version,
            rootPartitionKey,
            payloadVersionIdentifier);
    }

    private void SaveEvents(List<IEvent> events, Type originalType, string partitionKey, string sortableUniqueKey, bool fromInitial)
    {
        _hybridStoreManager.AddPartitionKey(partitionKey, sortableUniqueKey, fromInitial);
        foreach (var ev in events)
        {
            _documentTemporaryWriter.SaveAsync(ev, originalType).Wait();
        }
    }
}

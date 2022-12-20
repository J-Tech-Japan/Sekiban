using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Xunit.Abstractions;

namespace Sekiban.Core.Document;

public class DocumentRepositorySplitter : IDocumentRepository
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentTemporaryRepository _documentTemporaryRepository;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    private readonly HybridStoreManager _hybridStoreManager;
    private readonly ITestOutputHelper _outputHelper;
    public DocumentRepositorySplitter(
        IDocumentPersistentRepository documentPersistentRepository,
        IDocumentTemporaryRepository documentTemporaryRepository,
        HybridStoreManager hybridStoreManager,
        IDocumentTemporaryWriter documentTemporaryWriter,
        IAggregateSettings aggregateSettings, ITestOutputHelper outputHelper)
    {
        _documentPersistentRepository = documentPersistentRepository;
        _documentTemporaryRepository = documentTemporaryRepository;
        _hybridStoreManager = hybridStoreManager;
        _documentTemporaryWriter = documentTemporaryWriter;
        _aggregateSettings = aggregateSettings;
        _outputHelper = outputHelper;
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
            if (
                (string.IsNullOrWhiteSpace(sinceSortableUniqueId) && _hybridStoreManager.FromInitialForPartitionKey(partitionKey)) ||
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
                    var fromInitial = _hybridStoreManager.FromInitialForPartitionKey(partitionKey);

                    if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        SaveEvents(eventList, originalType, partitionKey, string.Empty, true);
                    }

                    if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId))
                    {
                        if (!hasPartitionKey)
                        {
                            SaveEvents(eventList, originalType, partitionKey, sinceSortableUniqueId, false);
                        }
                        else
                        {
                            if ((!string.IsNullOrWhiteSpace(sinceSortableUniqueIdInPartition) || !fromInitial) &&
                                string.Compare(
                                    sinceSortableUniqueIdInPartition!,
                                    sinceSortableUniqueId!,
                                    StringComparison.Ordinal) >
                                0)
                            {
                                SaveEvents(eventList, originalType, partitionKey, sinceSortableUniqueId, false);
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

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                payloadVersionIdentifier);
        }
        return await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                payloadVersionIdentifier) ??
            await _documentPersistentRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                payloadVersionIdentifier);
    }

    public Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return _documentTemporaryRepository.GetSnapshotByIdAsync(id, aggregatePayloadType, projectionPayloadType, partitionKey);
        }
        return _documentPersistentRepository.GetSnapshotByIdAsync(id, aggregatePayloadType, projectionPayloadType, partitionKey);
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

    public async Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        if (aggregateContainerGroup == AggregateContainerGroup.InMemoryContainer)
        {
            return false;
        }
        return await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
            aggregateId,
            aggregatePayloadType,
            projectionPayloadType,
            version,
            payloadVersionIdentifier);
    }

    private void SaveEvents(List<IEvent> events, Type originalType, string partitionKey, string sortableUniqueKey, bool fromInitial)
    {
        _hybridStoreManager.TestOutputHelper = _outputHelper;
        _hybridStoreManager.AddPartitionKey(partitionKey, sortableUniqueKey, fromInitial);
        foreach (var ev in events)
        {
            _documentTemporaryWriter.SaveAsync(ev, originalType).Wait();
        }
    }
}

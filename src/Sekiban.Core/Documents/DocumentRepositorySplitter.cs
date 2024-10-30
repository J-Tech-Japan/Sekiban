using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
namespace Sekiban.Core.Documents;

/// <summary>
///     Split repository depends on the Aggregate Type
///     Aggregate Payload can be marked with attribute
///     [AggregateContainerGroup(AggregateContainerGroup.Dissolvable)] dissolvable container
///     [AggregateContainerGroup(AggregateContainerGroup.InMemory)] in memory container (reset after the restart)
/// </summary>
public class DocumentRepositorySplitter : IDocumentRepository
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentTemporaryRepository _documentTemporaryRepository;
    private readonly IDocumentTemporaryWriter _documentTemporaryWriter;
    public DocumentRepositorySplitter(
        IDocumentPersistentRepository documentPersistentRepository,
        IDocumentTemporaryRepository documentTemporaryRepository,
        IDocumentTemporaryWriter documentTemporaryWriter,
        IAggregateSettings aggregateSettings)
    {
        _documentPersistentRepository = documentPersistentRepository;
        _documentTemporaryRepository = documentTemporaryRepository;
        _documentTemporaryWriter = documentTemporaryWriter;
        _aggregateSettings = aggregateSettings;
    }

    public async Task<ResultBox<UnitValue>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = eventRetrievalInfo.GetAggregateContainerGroup();
        if (aggregateContainerGroup == AggregateContainerGroup.InMemory)
        {
            return await _documentTemporaryRepository.GetEvents(eventRetrievalInfo, resultAction);
        }
        return await _documentPersistentRepository.GetEvents(eventRetrievalInfo, resultAction);
    }
    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        await GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new AggregateTypeStream(aggregatePayloadType),
                aggregateId,
                sinceSortableUniqueId),
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
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
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

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return aggregateContainerGroup == AggregateContainerGroup.InMemory
            ? await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                rootPartitionKey,
                payloadVersionIdentifier)
            : await _documentTemporaryRepository.GetLatestSnapshotForAggregateAsync(
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
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return aggregateContainerGroup == AggregateContainerGroup.InMemory
            ? await _documentTemporaryRepository.GetLatestSnapshotForMultiProjectionAsync(
                multiProjectionPayloadType,
                payloadVersionIdentifier,
                rootPartitionKey)
            : await _documentTemporaryRepository.GetLatestSnapshotForMultiProjectionAsync(
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
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return aggregateContainerGroup == AggregateContainerGroup.InMemory
            ? _documentTemporaryRepository.GetSnapshotByIdAsync(
                id,
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                partitionKey,
                rootPartitionKey)
            : _documentPersistentRepository.GetSnapshotByIdAsync(
                id,
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                partitionKey,
                rootPartitionKey);
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return aggregateContainerGroup != AggregateContainerGroup.InMemory &&
            await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                projectionPayloadType,
                version,
                rootPartitionKey,
                payloadVersionIdentifier);
    }

    private void SaveEvents(
        List<IEvent> events,
        Type originalType,
        string partitionKey,
        string sortableUniqueKey,
        bool fromInitial)
    {
        foreach (var ev in events)
        {
            _documentTemporaryWriter.SaveAsync(ev, originalType).Wait();
        }
    }
}

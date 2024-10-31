using ResultBoxes;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

public interface IDocumentRepository : IEventRepository
{
    Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction);

    Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier);

    Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions);

    Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier);

    Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string partitionKey,
        string rootPartitionKey);
}
public interface IEventRepository
{
    Task<ResultBox<bool>> GetEvents(EventRetrievalInfo eventRetrievalInfo, Action<IEnumerable<IEvent>> resultAction);
}

using Sekiban.Core.Event;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Document;

public interface IDocumentRepository
{
    Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction);

    Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction);

    Task GetAllEventsForAggregateAsync(
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string payloadVersionIdentifier);

    Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string payloadVersionIdentifier);

    Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey);
}
public interface IDocumentPersistentRepository : IDocumentRepository
{
    Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType);
}
public interface IDocumentTemporaryRepository : IDocumentRepository
{
    Task<bool> EventsForAggregateIdHasSortableUniqueIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sortableUniqueId);
}

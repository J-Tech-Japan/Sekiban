using Sekiban.Core.Event;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Document;

public interface IDocumentRepository
{
    Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task GetAllAggregateEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction);
    Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction);

    Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task GetAllAggregateEventsAsync(
        Type multipleProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType);

    Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version);

    Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type originalType, string partitionKey);
}
public interface IDocumentPersistentRepository : IDocumentRepository
{
    Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type originalType);
}
public interface IDocumentTemporaryRepository : IDocumentRepository
{
    Task<bool> AggregateEventsForAggregateIdHasSortableUniqueIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sortableUniqueId);
}

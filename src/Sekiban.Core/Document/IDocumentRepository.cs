using Sekiban.Core.Event;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Document;

public interface IDocumentRepository
{
    Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task GetAllEventStringsForAggregateIdAsync(
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

    Task GetAllEventsForAggregateAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction);

    Task GetAllEventsAsync(
        Type multiProjectionType,
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
    Task<bool> EventsForAggregateIdHasSortableUniqueIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sortableUniqueId);
}

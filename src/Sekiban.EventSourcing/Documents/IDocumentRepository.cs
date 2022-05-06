namespace Sekiban.EventSourcing.Documents;

public interface IDocumentRepository
{
    Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction);

    Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction);

    Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType);

    Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version);

    Task<SnapshotListDocument?> GetLatestSnapshotListForTypeAsync<T>(
        string? partitionKey,
        QueryListType queryListType = QueryListType.ActiveAndDeleted) where T : IAggregate;

    Task<SnapshotListChunkDocument?> GetSnapshotListChunkByIdAsync(Guid id, string partitionKey);

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

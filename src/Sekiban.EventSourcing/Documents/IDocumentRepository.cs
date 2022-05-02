using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Snapshots;
namespace Sekiban.EventSourcing.Documents;

public interface IDocumentRepository
{
    Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        Guid? sinceEventId,
        Action<IEnumerable<AggregateEvent>> resultAction);
    
    Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        Guid? sinceEventId,
        Action<IEnumerable<AggregateEvent>> resultAction);

    Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey);

    Task<SnapshotListDocument?> GetLatestSnapshotListForTypeAsync<T>(
        string? partitionKey,
        QueryListType queryListType = QueryListType.ActiveAndDeleted)
        where T : IAggregate;
    
    Task<SnapshotListChunkDocument?> GetSnapshotListChunkByIdAsync(
        Guid id,
        string partitionKey);


    Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Type originalType,
        string partitionKey);

}

public interface IDocumentPersistentRepository : IDocumentRepository {}
public interface IDocumentTemporaryRepository : IDocumentRepository {}


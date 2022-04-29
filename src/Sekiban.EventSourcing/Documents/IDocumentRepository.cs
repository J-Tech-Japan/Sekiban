using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Snapshots;
namespace Sekiban.EventSourcing.Documents;

public interface IDocumentRepository
{
    Task<IEnumerable<AggregateEvent>> GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        string? partitionKey = null,
        Guid? sinceEventId = null);

    Task<IEnumerable<AggregateEvent>> GetAllAggregateEventsForAggregateTypeAsync<T>(
        Guid? sinceEventId = null)
        where T : AggregateBase;

    Task<IEnumerable<AggregateEvent>> GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        Guid? sinceEventId = null);

    Task<IEnumerable<AggregateEvent>> GetAllCommandForTypeAsync<T>() where T : IAggregateCommand;

    Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
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
        string partitionKey);
}

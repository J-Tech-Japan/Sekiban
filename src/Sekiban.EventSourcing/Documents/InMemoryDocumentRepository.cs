using System.Collections.Concurrent;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentRepository : IDocumentTemporaryRepository
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;

    public InMemoryDocumentRepository(InMemoryDocumentStore inMemoryDocumentStore) =>
        _inMemoryDocumentStore = inMemoryDocumentStore;
    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        Guid? sinceEventId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        if (partitionKey == null)
        {
            
        }
        var list = partitionKey == null ? _inMemoryDocumentStore.GetAllEvents().Where(m => m.AggregateId == aggregateId).ToList() : _inMemoryDocumentStore.GetEventPartition(partitionKey).ToList();
        if (sinceEventId != null)
        {
            var index = list.FindIndex(m => m.Id == sinceEventId);
            resultAction(list.GetRange(index, list.Count - index).OrderBy(m => m.TimeStamp));
        }
        else
        {
            resultAction(list.OrderBy(m => m.TimeStamp));
        }
    }
    public async Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        Guid? sinceEventId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        var list = _inMemoryDocumentStore.GetAllEvents().Where(m => m.AggregateType == originalType.Name).ToList();
        if (sinceEventId != null)
        {
            var index = list.FindIndex(m => m.Id == sinceEventId);
            if (index == list.Count - 1)
            {
                resultAction(new List<AggregateEvent>());
            }
            else
            {
                resultAction(list.GetRange(index + 1, list.Count - index - 1).OrderBy(m => m.TimeStamp));
            }
        }
        else
        {
            resultAction(list.OrderBy(m => m.TimeStamp));
        }
    }
    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType, string? partitionKey) =>
        throw new NotImplementedException();
    public async Task<SnapshotListDocument?> GetLatestSnapshotListForTypeAsync<T>(
        string? partitionKey,
        QueryListType queryListType = QueryListType.ActiveAndDeleted) where T : IAggregate =>
        throw new NotImplementedException();
    public async Task<SnapshotListChunkDocument?> GetSnapshotListChunkByIdAsync(Guid id, string partitionKey) =>
        throw new NotImplementedException();
    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type originalType, string partitionKey) =>
        throw new NotImplementedException();
}

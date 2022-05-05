using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentRepository : IDocumentTemporaryRepository,
    IDocumentPersistentRepository
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCache _memoryCache;

    public InMemoryDocumentRepository(
        InMemoryDocumentStore inMemoryDocumentStore,
        IMemoryCache memoryCache)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _memoryCache = memoryCache;
    }
    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        if (partitionKey == null) { }
        var list = partitionKey == null
            ? _inMemoryDocumentStore.GetAllEvents()
                .Where(m => m.AggregateId == aggregateId)
                .ToList() : _inMemoryDocumentStore.GetEventPartition(partitionKey).ToList();
        if (sinceSortableUniqueId != null)
        {
            var index = list.Any(m => m.SortableUniqueId == sinceSortableUniqueId)
                ? list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId) : 0;
            resultAction(list.GetRange(index, list.Count - index).OrderBy(m => m.SortableUniqueId));
        }
        else
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        }
    }
    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type originalType)
    {
        await Task.CompletedTask;
        var partitionKeyFactory = new AggregateIdPartitionKeyFactory(aggregateId, originalType);
        var pk = partitionKeyFactory.GetPartitionKey(DocumentType.AggregateSnapshot);
        if (_memoryCache.TryGetValue<SnapshotDocument>(pk, out var sd))
        {
            return sd;
        }
        return null;
    }
    public async Task<SnapshotListDocument?> GetLatestSnapshotListForTypeAsync<T>(
        string? partitionKey,
        QueryListType queryListType = QueryListType.ActiveAndDeleted) where T : IAggregate =>
        throw new NotImplementedException();
    public async Task<SnapshotListChunkDocument?> GetSnapshotListChunkByIdAsync(
        Guid id,
        string partitionKey) =>
        throw new NotImplementedException();
    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Type originalType,
        string partitionKey) =>
        throw new NotImplementedException();
    public async Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        var list = _inMemoryDocumentStore.GetAllEvents()
            .Where(m => m.AggregateType == originalType.Name)
            .ToList();
        if (sinceSortableUniqueId != null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<AggregateEvent>());
            }
            else
            {
                resultAction(
                    list.GetRange(index + 1, list.Count - index - 1)
                        .OrderBy(m => m.SortableUniqueId));
            }
        }
        else
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        }
    }

    public Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version)
    {
        return Task.FromResult(false);
    }
}

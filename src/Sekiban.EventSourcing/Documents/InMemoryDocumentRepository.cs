using Microsoft.Extensions.Caching.Memory;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentRepository : IDocumentTemporaryRepository, IDocumentPersistentRepository
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCache _memoryCache;

    public InMemoryDocumentRepository(InMemoryDocumentStore inMemoryDocumentStore, IMemoryCache memoryCache)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _memoryCache = memoryCache;
    }
    public Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type originalType) =>
        throw new NotImplementedException();
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
            ? _inMemoryDocumentStore.GetAllEvents().Where(m => m.AggregateId == aggregateId).ToList()
            : _inMemoryDocumentStore.GetEventPartition(partitionKey).OrderBy(m => m.SortableUniqueId).ToList();
        if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        } else
        {
            var index = list.Any(m => m.SortableUniqueId == sinceSortableUniqueId)
                ? list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId)
                : 0;
            if (index == list.Count - 1) { resultAction(new List<AggregateEvent>()); }
            resultAction(
                list.GetRange(index, list.Count - index).Where(m => m.SortableUniqueId != sinceSortableUniqueId).OrderBy(m => m.SortableUniqueId));
        }
    }
    public async Task GetAllAggregateEventsAsync(
        Type multipleProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        var list = _inMemoryDocumentStore.GetAllEvents().ToList();
        if (sinceSortableUniqueId != null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<AggregateEvent>());
            } else
            {
                resultAction(
                    list.GetRange(index + 1, list.Count - index - 1)
                        .Where(m => targetAggregateNames.Count == 0 || targetAggregateNames.Contains(m.AggregateType))
                        .OrderBy(m => m.SortableUniqueId));
            }
        } else
        {
            resultAction(
                list.Where(m => targetAggregateNames.Count == 0 || targetAggregateNames.Contains(m.AggregateType)).OrderBy(m => m.SortableUniqueId));
        }
    }
    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType)
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
    public Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type originalType, string partitionKey) =>
        throw new NotImplementedException();
    public async Task<bool> AggregateEventsForAggregateIdHasSortableUniqueIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sortableUniqueId)
    {
        await Task.CompletedTask;
        if (partitionKey == null) { return false; }
        var list = _inMemoryDocumentStore.GetEventPartition(partitionKey).ToList();
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return false;
        }
        return list.Any(m => m.SortableUniqueId == sortableUniqueId);
    }
    public async Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        var list = _inMemoryDocumentStore.GetAllEvents().Where(m => m.AggregateType == originalType.Name).ToList();
        if (sinceSortableUniqueId != null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<AggregateEvent>());
            } else
            {
                resultAction(list.GetRange(index + 1, list.Count - index - 1).OrderBy(m => m.SortableUniqueId));
            }
        } else
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        }
    }

    public Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version) =>
        Task.FromResult(false);
}
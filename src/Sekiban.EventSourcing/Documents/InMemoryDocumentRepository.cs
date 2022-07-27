using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Settings;
namespace Sekiban.EventSourcing.Documents;

public class InMemoryDocumentRepository : IDocumentTemporaryRepository, IDocumentPersistentRepository
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IMemoryCache _memoryCache;
    private readonly IServiceProvider _serviceProvider;
    public InMemoryDocumentRepository(InMemoryDocumentStore inMemoryDocumentStore, IMemoryCache memoryCache, IServiceProvider serviceProvider)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
    }
    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type originalType)
    {
        await Task.CompletedTask;
        return new List<SnapshotDocument>();
    }
    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        if (partitionKey == null) { }
        var list = partitionKey == null
            ? _inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).Where(m => m.AggregateId == aggregateId).ToList()
            : _inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier).OrderBy(m => m.SortableUniqueId).ToList();
        if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        } else
        {
            var index = list.Any(m => m.SortableUniqueId == sinceSortableUniqueId)
                ? list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId)
                : 0;
            if (index == list.Count - 1) { resultAction(new List<IAggregateEvent>()); }
            resultAction(
                list.GetRange(index, list.Count - index).Where(m => m.SortableUniqueId != sinceSortableUniqueId).OrderBy(m => m.SortableUniqueId));
        }
    }
    public async Task GetAllAggregateEventsAsync(
        Type multipleProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        var list = _inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).ToList();
        if (sinceSortableUniqueId != null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<IAggregateEvent>());
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
        if (_memoryCache.TryGetValue<SnapshotDocument>(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, originalType), out var sd))
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
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        if (partitionKey == null) { return false; }
        var list = _inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier).ToList();
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return false;
        }
        return list.Any(m => m.SortableUniqueId == sortableUniqueId);
    }
    public async Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        await Task.CompletedTask;
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        var list = _inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).Where(m => m.AggregateType == originalType.Name).ToList();
        if (sinceSortableUniqueId != null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<IAggregateEvent>());
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

using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Event;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

public class InMemoryDocumentRepository : IDocumentTemporaryRepository, IDocumentPersistentRepository
{
    private readonly InMemoryDocumentStore _inMemoryDocumentStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISnapshotDocumentCache _snapshotDocumentCache;

    public InMemoryDocumentRepository(
        InMemoryDocumentStore inMemoryDocumentStore,
        IServiceProvider serviceProvider,
        ISnapshotDocumentCache snapshotDocumentCache)
    {
        _inMemoryDocumentStore = inMemoryDocumentStore;
        _serviceProvider = serviceProvider;
        _snapshotDocumentCache = snapshotDocumentCache;
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType)
    {
        await Task.CompletedTask;
        return new List<SnapshotDocument>();
    }

    public async Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        var list = partitionKey is null
            ? _inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).Where(m => m.AggregateId == aggregateId).ToList()
            : _inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier)
                .OrderBy(m => m.SortableUniqueId)
                .ToList();
        if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        }
        else
        {
            var index = list.Any(m => m.SortableUniqueId == sinceSortableUniqueId)
                ? list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId)
                : 0;
            if (index == list.Count - 1)
            {
                resultAction(new List<IEvent>());
            }
            resultAction(
                list.GetRange(index, list.Count - index)
                    .Where(m => m.SortableUniqueId != sinceSortableUniqueId)
                    .OrderBy(m => m.SortableUniqueId));
        }
    }

    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction)
    {
        await GetAllEventsForAggregateIdAsync(
            aggregateId,
            aggregatePayloadType,
            partitionKey,
            sinceSortableUniqueId,
            events =>
            {
                resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
            });
    }

    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<string>> resultAction)
    {
        await Task.CompletedTask;
        resultAction(new List<string>());
    }

    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        var list = _inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).ToList();
        if (sinceSortableUniqueId is not null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<IEvent>());
            }
            else
            {
                resultAction(
                    list.GetRange(index + 1, list.Count - index - 1)
                        .Where(m => targetAggregateNames.Count == 0 || targetAggregateNames.Contains(m.AggregateType))
                        .OrderBy(m => m.SortableUniqueId));
            }
        }
        else
        {
            resultAction(
                list.Where(m => targetAggregateNames.Count == 0 || targetAggregateNames.Contains(m.AggregateType))
                    .OrderBy(m => m.SortableUniqueId));
        }
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string payloadVersionIdentifier)
    {
        await Task.CompletedTask;
        if (_snapshotDocumentCache.Get(aggregateId, projectionPayloadType, projectionPayloadType) is { } snapshotDocument)
        {
            return snapshotDocument;
        }
        return null;
    }

    public Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey) =>
        throw new NotImplementedException();

    public async Task<bool> EventsForAggregateIdHasSortableUniqueIdAsync(
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

        if (partitionKey is null)
        {
            return false;
        }
        var list = _inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier).ToList();
        if (string.IsNullOrWhiteSpace(sortableUniqueId))
        {
            return false;
        }
        return list.Any(m => m.SortableUniqueId == sortableUniqueId);
    }

    public async Task GetAllEventsForAggregateAsync(
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        await Task.CompletedTask;
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        var list = _inMemoryDocumentStore.GetAllEvents(sekibanIdentifier)
            .Where(m => m.AggregateType == aggregatePayloadType.Name)
            .ToList();
        if (sinceSortableUniqueId is not null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(new List<IEvent>());
            }
            else
            {
                resultAction(list.GetRange(index + 1, list.Count - index - 1).OrderBy(m => m.SortableUniqueId));
            }
        }
        else
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        }
    }

    public Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string payloadVersionIdentifier) =>
        Task.FromResult(false);
}
using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
namespace Sekiban.Core.Documents;

/// <summary>
///     In memory Document Repository.
///     Developer does not need to use this class
///     Use interface <see cref="IDocumentRepository" />
/// </summary>
public class InMemoryDocumentRepository(
    InMemoryDocumentStore inMemoryDocumentStore,
    IServiceProvider serviceProvider,
    ISnapshotDocumentCache snapshotDocumentCache) : IDocumentTemporaryRepository,
    IDocumentPersistentRepository,
    IEventPersistentRepository,
    IEventTemporaryRepository
{

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        await Task.CompletedTask;
        return [];
    }
    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        await Task.CompletedTask;
        resultAction(Enumerable.Empty<string>());
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        await Task.CompletedTask;
        return snapshotDocumentCache.Get(aggregateId, projectionPayloadType, projectionPayloadType, rootPartitionKey) is
            { } snapshotDocument
            ? snapshotDocument
            : null;
    }
    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
    {
        await Task.CompletedTask;
        return default;
    }

    public Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string partitionKey,
        string rootPartitionKey) =>
        throw new NotImplementedException();

    public async Task<bool> EventsForAggregateIdHasSortableUniqueIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sortableUniqueId)
    {
        await Task.CompletedTask;
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        if (partitionKey is null)
        {
            return false;
        }
        var list = inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier).ToList();
        return !string.IsNullOrWhiteSpace(sortableUniqueId) && list.Exists(m => m.SortableUniqueId == sortableUniqueId);
    }

    public Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier) =>
        Task.FromResult(false);

    public async Task<ResultBox<bool>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        if (eventRetrievalInfo.GetIsPartition())
        {
            var partitionKey = eventRetrievalInfo.GetPartitionKey();
            if (partitionKey.IsSuccess)
            {
                var array = inMemoryDocumentStore.GetEventPartition(partitionKey.GetValue(), sekibanIdentifier);
                var list = eventRetrievalInfo.Order == RetrieveEventOrder.OldToNew
                    ? array.OrderBy(m => m.SortableUniqueId).ToList()
                    : array.OrderByDescending(m => m.SortableUniqueId).ToList();
                if (eventRetrievalInfo.SinceSortableUniqueId.HasValue)
                {
                    var index = list.FindIndex(
                        m => m.SortableUniqueId == eventRetrievalInfo.SinceSortableUniqueId.GetValue());
                    if (index == list.Count - 1)
                    {
                        resultAction(Enumerable.Empty<IEvent>());
                    } else
                    {
                        var range = list.GetRange(index + 1, list.Count - index - 1);
                        range = eventRetrievalInfo.MaxCount.HasValue
                            ? range.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList()
                            : range;
                        resultAction(range);
                    }
                } else
                {
                    list = eventRetrievalInfo.MaxCount.HasValue
                        ? list.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList()
                        : list;
                    resultAction(list);
                }
            }
        } else
        {
            var enumerable = inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).AsEnumerable();
            if (eventRetrievalInfo.HasAggregateStream())
            {
                var aggregateStream = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
                enumerable = enumerable.Where(m => aggregateStream.Contains(m.AggregateType));
            }
            if (eventRetrievalInfo.HasRootPartitionKey())
            {
                enumerable = enumerable.Where(
                    m => m.RootPartitionKey == eventRetrievalInfo.RootPartitionKey.GetValue());
            }
            var list = eventRetrievalInfo.Order == RetrieveEventOrder.OldToNew
                ? enumerable.OrderBy(m => m.SortableUniqueId).ToList()
                : enumerable.OrderByDescending(m => m.SortableUniqueId).ToList();
            if (eventRetrievalInfo.SinceSortableUniqueId.HasValue)
            {
                var index = list.FindIndex(
                    m => m.SortableUniqueId == eventRetrievalInfo.SinceSortableUniqueId.GetValue());
                if (index == list.Count - 1)
                {
                    resultAction(Enumerable.Empty<IEvent>());
                } else
                {
                    var range = list.GetRange(index + 1, list.Count - index - 1);
                    range = eventRetrievalInfo.MaxCount.HasValue
                        ? range.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList()
                        : range;
                    resultAction(range);
                }
            } else
            {
                list = eventRetrievalInfo.MaxCount.HasValue
                    ? list.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList()
                    : list;
                resultAction(list);
            }
        }
        return true;
    }
}

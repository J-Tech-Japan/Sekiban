using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Cache;
using Sekiban.Core.Events;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
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
    ISnapshotDocumentCache snapshotDocumentCache) : IDocumentTemporaryRepository, IDocumentPersistentRepository
{

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        await Task.CompletedTask;
        return new List<SnapshotDocument>();
    }

    public async Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        var list = partitionKey is null
            ? inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).Where(m => m.AggregateId == aggregateId).ToList()
            : inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier).OrderBy(m => m.SortableUniqueId).ToList();
        if (string.IsNullOrWhiteSpace(sinceSortableUniqueId))
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        } else
        {
            var index = list.Exists(m => m.SortableUniqueId == sinceSortableUniqueId)
                ? list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId)
                : 0;
            if (index == list.Count - 1)
            {
                resultAction(Enumerable.Empty<IEvent>());
            }
            resultAction(
                list.GetRange(index, list.Count - index).Where(m => m.SortableUniqueId != sinceSortableUniqueId).OrderBy(m => m.SortableUniqueId));
        }
    }

    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        await GetAllEventsForAggregateIdAsync(
            aggregateId,
            aggregatePayloadType,
            partitionKey,
            sinceSortableUniqueId,
            rootPartitionKey,
            events =>
            {
                resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
            });
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

    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;
        await Task.CompletedTask;
        var list = inMemoryDocumentStore.GetAllEvents(sekibanIdentifier)
            .Where(m => rootPartitionKey == IMultiProjectionService.ProjectionAllRootPartitions || m.RootPartitionKey == rootPartitionKey)
            .ToList();

        if (sinceSortableUniqueId is not null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(Enumerable.Empty<IEvent>());
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

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        await Task.CompletedTask;
        return snapshotDocumentCache.Get(aggregateId, projectionPayloadType, projectionPayloadType, rootPartitionKey) is { } snapshotDocument
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

    public async Task GetAllEventsForAggregateAsync(
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        await Task.CompletedTask;
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;

        var list = inMemoryDocumentStore.GetAllEvents(sekibanIdentifier)
            .Where(m => m.AggregateType == aggregatePayloadType.Name && m.RootPartitionKey == rootPartitionKey)
            .ToList();
        if (sinceSortableUniqueId is not null)
        {
            var index = list.FindIndex(m => m.SortableUniqueId == sinceSortableUniqueId);
            if (index == list.Count - 1)
            {
                resultAction(Enumerable.Empty<IEvent>());
            } else
            {
                resultAction(list.GetRange(index + 1, list.Count - index - 1).OrderBy(m => m.SortableUniqueId));
            }
        } else
        {
            resultAction(list.OrderBy(m => m.SortableUniqueId));
        }
    }

    public Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier) =>
        Task.FromResult(false);
}

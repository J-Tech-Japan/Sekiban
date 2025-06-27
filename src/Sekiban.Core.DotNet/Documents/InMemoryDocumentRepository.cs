using Microsoft.Extensions.DependencyInjection;
using ResultBoxes;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
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
        await Task.CompletedTask;

        var range = ListEvents(eventRetrievalInfo)
            .Where(m => eventRetrievalInfo.SortableIdCondition.InsideOfRange(m.SortableUniqueId));
        if (eventRetrievalInfo.MaxCount.HasValue)
        {
            range = range.Take(eventRetrievalInfo.MaxCount.Value);
        }

        resultAction(range.ToList());
        return true;
    }

    private List<IEvent> ListEvents(EventRetrievalInfo eventRetrievalInfo)
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        var sekibanIdentifier = string.IsNullOrWhiteSpace(sekibanContext?.SettingGroupIdentifier)
            ? string.Empty
            : sekibanContext.SettingGroupIdentifier;


        if (eventRetrievalInfo.GetIsPartition())
        {
            var partitionKey = eventRetrievalInfo.GetPartitionKey().UnwrapBox();
            var array = inMemoryDocumentStore.GetEventPartition(partitionKey, sekibanIdentifier);

            return array
                .OrderBy(m => m.SortableUniqueId)
                .ToList();
        }


        var enumerable = inMemoryDocumentStore.GetAllEvents(sekibanIdentifier).AsEnumerable();

        if (eventRetrievalInfo.HasAggregateStream())
        {
            var aggregateStream = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
            enumerable = enumerable
                .Where(m => aggregateStream.Contains(m.AggregateType));
        }

        if (eventRetrievalInfo.HasRootPartitionKey())
        {
            var rootPartitionKey = eventRetrievalInfo.RootPartitionKey.GetValue();
            enumerable = enumerable
                .Where(m => m.RootPartitionKey == rootPartitionKey);
        }

        return enumerable
            .OrderBy(x => x.SortableUniqueId)
            .ToList();
    }
}

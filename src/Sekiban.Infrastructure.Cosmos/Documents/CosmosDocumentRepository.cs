using Microsoft.Azure.Cosmos.Linq;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
// ReSharper disable StringCompareToIsCultureSpecific

namespace Sekiban.Infrastructure.Cosmos.Documents;

/// <summary>
///     Retrieve documents from CosmosDB
/// </summary>
/// <param name="cosmosDbFactory"></param>
/// <param name="registeredEventTypes"></param>
/// <param name="singleProjectionSnapshotAccessor"></param>
public class CosmosDocumentRepository(
    ICosmosDbFactory cosmosDbFactory,
    RegisteredEventTypes registeredEventTypes,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor)
    : IDocumentPersistentRepository, IEventPersistentRepository
{
    private const int DefaultOptionsMax = -1;

    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return await cosmosDbFactory.CosmosActionAsync(
            DocumentType.MultiProjectionSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions { MaxItemCount = 1 };
                var partitionKey = PartitionKeyGenerator.ForMultiProjectionSnapshot(
                    multiProjectionPayloadType,
                    rootPartitionKey);
                var query = container
                    .GetItemLinqQueryable<MultiProjectionSnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.MultiProjectionSnapshot &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier &&
                            b.PartitionKey == partitionKey)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<MultiProjectionSnapshotDocument>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
                    var next = await feedIterator.ReadNextAsync();
                    if (next.Count > 0)
                    {
                        return next.First();
                    }
                }
                return null;
            });
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var list = new List<SnapshotDocument>();
                var options = new QueryRequestOptions
                {
                    PartitionKey = CosmosPartitionGenerator.ForSingleProjectionSnapshot(
                        rootPartitionKey,
                        aggregatePayloadType,
                        projectionPayloadType,
                        aggregateId)
                };
                var query = container
                    .GetItemLinqQueryable<SnapshotDocument>()
                    .Where(b => b.DocumentType == DocumentType.AggregateSnapshot && b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        if (obj is null) { continue; }
                        var filled = await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(obj);
                        if (filled is not null)
                        {
                            list.Add(filled);
                        }
                    }
                }
                return list;
            });
    }
    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await cosmosDbFactory.CosmosActionAsync(
            DocumentType.Command,
            aggregateContainerGroup,
            async container =>
            {
                var options = CreateDefaultOptions();
                options.PartitionKey = CosmosPartitionGenerator.ForCommand(
                    rootPartitionKey,
                    aggregatePayloadType,
                    aggregateId);

                var query = container
                    .GetItemLinqQueryable<IAggregateDocument>()
                    .Where(b => b.DocumentType == DocumentType.Command && b.AggregateId == aggregateId);
                query = sinceSortableUniqueId is not null
                    ? query
                        .Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0)
                        .OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var commands = new List<string>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    commands.AddRange(
                        (from item in response
                         where sinceSortableUniqueId is null ||
                             !new SortableUniqueIdValue(item.SortableUniqueId).IsEarlierThan(sinceSortableUniqueId)
                         select SekibanJsonHelper.Serialize(item)).Cast<string>());
                }

                resultAction(commands);
            });
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        int version,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions
                {
                    PartitionKey = CosmosPartitionGenerator.ForSingleProjectionSnapshot(
                        rootPartitionKey,
                        aggregatePayloadType,
                        projectionPayloadType,
                        aggregateId)
                };
                var query = container
                    .GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId &&
                            b.SavedVersion == version &&
                            b.DocumentTypeName == projectionPayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
                    var searched = await feedIterator.ReadNextAsync();
                    if (searched.Count > 0)
                    {
                        return true;
                    }
                }
                return false;
            });
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions
                {
                    PartitionKey = CosmosPartitionGenerator.ForSingleProjectionSnapshot(
                        rootPartitionKey,
                        aggregatePayloadType,
                        projectionPayloadType,
                        aggregateId),
                    MaxItemCount = 1
                };
                var query = container
                    .GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId &&
                            b.AggregateType == aggregatePayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        if (obj is null) { continue; }
                        return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(obj);
                    }
                }
                return null;
            });
    }

    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string partitionKey,
        string rootPartitionKey)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var response = await container.ReadItemAsync<SnapshotDocument>(
                    id.ToString(),
                    CosmosPartitionGenerator.ForSingleProjectionSnapshot(
                        rootPartitionKey,
                        aggregatePayloadType,
                        projectionPayloadType,
                        aggregateId));
                return response.Resource is null
                    ? null
                    : await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(response.Resource);
            });
    }

    public async Task<ResultBox<bool>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction)
    {
        await cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            eventRetrievalInfo.GetAggregateContainerGroup(),
            async container =>
            {
                if (eventRetrievalInfo.GetIsPartition())
                {
                    var options = CreateDefaultOptions();
                    options.PartitionKey = CosmosPartitionGenerator.ForEventGroup(
                        eventRetrievalInfo.RootPartitionKey.GetValue(),
                        eventRetrievalInfo.AggregateStream.GetValue().GetSingleStreamName().UnwrapBox(),
                        eventRetrievalInfo.AggregateId.GetValue());
                    var query = container.GetItemLinqQueryable<IEvent>();
                    query = eventRetrievalInfo.SortableIdCondition switch
                    {
                        (SinceSortableIdCondition since) => query
                            .Where(m => m.SortableUniqueId.CompareTo(since.SortableUniqueId.Value) > 0)
                            .OrderBy(m => m.SortableUniqueId),
                        SortableIdConditionNone => query.OrderBy(m => m.SortableUniqueId),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    var feedIterator = container.GetItemQueryIterator<dynamic>(
                        query.ToQueryDefinition(),
                        null,
                        options);
                    var events = new List<IEvent>();
                    while (feedIterator.HasMoreResults)
                    {
                        var response = await feedIterator.ReadNextAsync();
                        var toAdds = ProcessEvents(response, eventRetrievalInfo.SortableIdCondition);
                        events.AddRange(toAdds);
                        if (eventRetrievalInfo.MaxCount.HasValue &&
                            events.Count > eventRetrievalInfo.MaxCount.GetValue())
                        {
                            events = events.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList();
                            break;
                        }
                    }
                    resultAction(events);
                } else
                {
                    var options = CreateDefaultOptions();

                    var query = container.GetItemLinqQueryable<IEvent>().AsQueryable();
                    if (eventRetrievalInfo.HasAggregateStream())
                    {
                        var aggregates = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
                        query = query.Where(m => aggregates.Contains(m.AggregateType));
                    }
                    if (eventRetrievalInfo.HasRootPartitionKey())
                    {
                        query = query.Where(m => m.RootPartitionKey == eventRetrievalInfo.RootPartitionKey.GetValue());
                    }
                    query = eventRetrievalInfo.SortableIdCondition switch
                    {
                        (SinceSortableIdCondition since) => query
                            .Where(m => m.SortableUniqueId.CompareTo(since.SortableUniqueId.Value) > 0)
                            .OrderBy(m => m.SortableUniqueId),
                        (SortableIdConditionNone _) => query.OrderBy(m => m.SortableUniqueId),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    var feedIterator = container.GetItemQueryIterator<dynamic>(
                        query.ToQueryDefinition(),
                        null,
                        options);
                    var events = new List<IEvent>();
                    while (feedIterator.HasMoreResults)
                    {
                        var response = await feedIterator.ReadNextAsync();
                        var toAdds = ProcessEvents(response, eventRetrievalInfo.SortableIdCondition);
                        events.AddRange(toAdds);
                        if (eventRetrievalInfo.MaxCount.HasValue &&
                            events.Count > eventRetrievalInfo.MaxCount.GetValue())
                        {
                            events = events.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList();
                            break;
                        }
                    }
                    resultAction(events);
                }
            });
        return true;
    }

    private List<IEvent> ProcessEvents(IEnumerable<dynamic> response, ISortableIdCondition sortableIdCondition)
    {
        var events = new List<IEvent>();
        foreach (var item in response)
        {
            // pick out one item
            if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
            {
                continue;
            }

            var toAdd = (registeredEventTypes
                        .RegisteredTypes
                        .Where(m => m.Name == typeName)
                        .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                        .FirstOrDefault(m => m is not null) ??
                    EventHelper.GetUnregisteredEvent(item)) ??
                throw new SekibanUnregisteredEventFoundException();
            if (sortableIdCondition.OutsideOfRange(toAdd.GetSortableUniqueId()))
            {
                continue;
            }

            events.Add(toAdd);
        }

        return events;
    }
    private static QueryRequestOptions CreateDefaultOptions() =>
        new()
        {
            MaxConcurrency = DefaultOptionsMax, MaxItemCount = DefaultOptionsMax,
            MaxBufferedItemCount = DefaultOptionsMax
        };
}

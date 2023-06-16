using Microsoft.Azure.Cosmos.Linq;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
// ReSharper disable StringCompareToIsCultureSpecific

namespace Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;

public class CosmosDocumentRepository : IDocumentPersistentRepository
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly RegisteredEventTypes _registeredEventTypes;
    private readonly ISingleProjectionSnapshotAccessor _singleProjectionSnapshotAccessor;
    public CosmosDocumentRepository(
        CosmosDbFactory cosmosDbFactory,
        RegisteredEventTypes registeredEventTypes,
        ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _registeredEventTypes = registeredEventTypes;
        _singleProjectionSnapshotAccessor = singleProjectionSnapshotAccessor;
    }

    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions { MaxConcurrency = -1, MaxItemCount = -1, MaxBufferedItemCount = -1 };
                var query = targetAggregateNames.Count switch
                {
                    0 => container.GetItemLinqQueryable<IEvent>(),
                    _ => container.GetItemLinqQueryable<IEvent>().Where(b => targetAggregateNames.Contains(b.AggregateType))
                };
                if (!string.IsNullOrEmpty(sinceSortableUniqueId))
                {
                    query = query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0);
                }
                if (!string.IsNullOrEmpty(rootPartitionKey))
                {
                    query = query.Where(m => m.RootPartitionKey == rootPartitionKey);
                }
                query = query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    var events = new List<IEvent>();
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one item
                        if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                        {
                            continue;
                        }

                        var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                                .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                                .FirstOrDefault(m => m is not null) ??
                            EventHelper.GetUnregisteredEvent(item);
                        if (toAdd is null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.GetSortableUniqueId().EarlierThan(sinceSortableUniqueId))
                        {
                            continue;
                        }

                        events.Add(toAdd);
                    }
                    resultAction(events.OrderBy(m => m.SortableUniqueId));
                }
            });
    }
    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.MultiProjectionSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions { MaxItemCount = 1 };
                var partitionKey = PartitionKeyGenerator.ForMultiProjectionSnapshot(multiProjectionPayloadType, rootPartitionKey);
                var query = container.GetItemLinqQueryable<MultiProjectionSnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.MultiProjectionSnapshot &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier &&
                            b.PartitionKey == partitionKey)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<MultiProjectionSnapshotDocument>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        return obj;
                    }
                }
                return null;
            });
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
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
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(b => b.DocumentType == DocumentType.AggregateSnapshot && b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        if (obj is null) { continue; }
                        var filled = await _singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(obj);
                        if (filled is not null)
                        {
                            list.Add(filled);
                        }
                    }
                }
                return list;
            });
    }

    public async Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var types = _registeredEventTypes.RegisteredTypes;
                var options = new QueryRequestOptions
                {
                    MaxConcurrency = -1,
                    MaxItemCount = -1,
                    MaxBufferedItemCount = -1,
                    PartitionKey = CosmosPartitionGenerator.ForEvent(rootPartitionKey, aggregatePayloadType, aggregateId)
                };
                var query = container.GetItemLinqQueryable<IEvent>();
                query = sinceSortableUniqueId is not null
                    ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0).OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var events = new List<IEvent>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                        {
                            continue;
                        }

                        var toAdd = types.Where(m => m.Name == typeName)
                                .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                                .FirstOrDefault(m => m is not null) ??
                            EventHelper.GetUnregisteredEvent(item);
                        if (toAdd is null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.GetSortableUniqueId().EarlierThan(sinceSortableUniqueId))
                        {
                            continue;
                        }
                        events.Add(toAdd);
                    }
                }

                resultAction(events.OrderBy(m => m.SortableUniqueId));
            });
    }

    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Command,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions { MaxConcurrency = -1, MaxItemCount = -1, MaxBufferedItemCount = -1 };
                options.PartitionKey = CosmosPartitionGenerator.ForCommand(rootPartitionKey, aggregatePayloadType, aggregateId);

                var query = container.GetItemLinqQueryable<IAggregateDocument>()
                    .Where(b => b.DocumentType == DocumentType.Command && b.AggregateId == aggregateId);
                query = sinceSortableUniqueId is not null
                    ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0).OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var commands = new List<string>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (sinceSortableUniqueId is not null && new SortableUniqueIdValue(item.SortableUniqueId).EarlierThan(sinceSortableUniqueId))
                        {
                            continue;
                        }
                        commands.Add(SekibanJsonHelper.Serialize(item));
                    }
                }

                resultAction(commands);
            });
    }

    public async Task GetAllEventsForAggregateAsync(
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions { MaxConcurrency = -1, MaxItemCount = -1, MaxBufferedItemCount = -1 };
                var query = container.GetItemLinqQueryable<IEvent>()
                    .Where(b => b.DocumentType == DocumentType.Event && b.AggregateType == aggregatePayloadType.Name);
                if (rootPartitionKey != IMultiProjectionService.ProjectionAllRootPartitions)
                {
                    query = query.Where(m => m.RootPartitionKey == rootPartitionKey);
                }

                query = sinceSortableUniqueId is not null
                    ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0).OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderByDescending(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var events = new List<IEvent>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                        {
                            continue;
                        }

                        var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                                .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                                .FirstOrDefault(m => m is not null) ??
                            EventHelper.GetUnregisteredEvent(item);
                        if (toAdd is null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.GetSortableUniqueId().EarlierThan(sinceSortableUniqueId))
                        {
                            continue;
                        }
                        events.Add(toAdd);
                    }
                }

                resultAction(events.OrderBy(m => m.SortableUniqueId));
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
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
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
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId &&
                            b.SavedVersion == version &&
                            b.DocumentTypeName == projectionPayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var _ in await feedIterator.ReadNextAsync())
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
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
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
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId &&
                            b.AggregateType == aggregatePayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        if (obj is null) { continue; }
                        return await _singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(obj);
                    }
                }
                return null;
            });
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

    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string partitionKey,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var response = await container.ReadItemAsync<SnapshotDocument>(
                    id.ToString(),
                    CosmosPartitionGenerator.ForSingleProjectionSnapshot(rootPartitionKey, aggregatePayloadType, projectionPayloadType, aggregateId));
                if (response.Resource is null) { return null; }
                return await _singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(response.Resource);
            });
    }
}

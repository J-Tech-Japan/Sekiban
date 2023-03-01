using Microsoft.Azure.Cosmos.Linq;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
// ReSharper disable StringCompareToIsCultureSpecific

namespace Sekiban.Infrastructure.Cosmos.DomainCommon.EventSourcings;

public class CosmosDocumentRepository : IDocumentPersistentRepository
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly RegisteredEventTypes _registeredEventTypes;
    private readonly ISekibanContext _sekibanContext;

    public CosmosDocumentRepository(
        CosmosDbFactory cosmosDbFactory,
        RegisteredEventTypes registeredEventTypes,
        ISekibanContext sekibanContext)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _registeredEventTypes = registeredEventTypes;
        _sekibanContext = sekibanContext;
    }

    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions
                {
                    MaxConcurrency = -1,
                    MaxItemCount = -1,
                    MaxBufferedItemCount = -1
                };
                var query = targetAggregateNames.Count switch
                {
                    0 => container.GetItemLinqQueryable<IEvent>(),
                    _ => container.GetItemLinqQueryable<IEvent>()
                        .Where(b => targetAggregateNames.Contains(b.AggregateType))
                };
                if (!string.IsNullOrEmpty(sinceSortableUniqueId))
                {
                    query = query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0);
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
                        if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string
                            typeName)
                        {
                            continue;
                        }

                        var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                                .Select(
                                    m =>
                                        SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                                .FirstOrDefault(m => m is not null) ??
                            EventHelper.GetUnregisteredEvent(item);
                        if (toAdd is null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                            toAdd.GetSortableUniqueId().EarlierThan(sinceSortableUniqueId))
                        {
                            continue;
                        }

                        events.Add(toAdd);
                    }
                    resultAction(events.OrderBy(m => m.SortableUniqueId));
                }
            });
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
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
                    PartitionKey =
                        new PartitionKey(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType)),
                    MaxItemCount = 1
                };
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId &&
                            b.DocumentTypeName == projectionPayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator =
                    container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
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
    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.MultiProjectionSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions
                {
                    PartitionKey =
                        new PartitionKey(PartitionKeyGenerator.ForMultiProjectionSnapshot(multiProjectionPayloadType)),
                    MaxItemCount = 1
                };
                var query = container.GetItemLinqQueryable<MultiProjectionSnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.MultiProjectionSnapshot &&
                            b.DocumentTypeName == multiProjectionPayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator =
                    container.GetItemQueryIterator<MultiProjectionSnapshotDocument>(query.ToQueryDefinition(), null, options);
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

    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var response =
                    await container.ReadItemAsync<SnapshotDocument>(id.ToString(), new PartitionKey(partitionKey));
                return response.Resource;
            });
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType)
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
                    PartitionKey =
                        new PartitionKey(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType))
                };
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(b => b.DocumentType == DocumentType.AggregateSnapshot && b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator =
                    container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        list.Add(obj);
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
                    MaxBufferedItemCount = -1
                };
                if (partitionKey is not null)
                {
                    options.PartitionKey = new PartitionKey(partitionKey);
                }

                var query = container.GetItemLinqQueryable<IEvent>()
                    .Where(b => b.DocumentType == DocumentType.Event && b.AggregateId == aggregateId);
                query = sinceSortableUniqueId is not null
                    ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0)
                        .OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var events = new List<IEvent>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string
                            typeName)
                        {
                            continue;
                        }

                        var toAdd = types.Where(m => m.Name == typeName)
                                .Select(
                                    m =>
                                        SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                                .FirstOrDefault(m => m is not null) ??
                            EventHelper.GetUnregisteredEvent(item);
                        ;
                        if (toAdd is null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                            toAdd.GetSortableUniqueId().EarlierThan(sinceSortableUniqueId))
                        {
                            continue;
                        }
                        events.Add(toAdd);
                    }
                }

                resultAction(events.OrderBy(m => m.SortableUniqueId));
            });
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
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Command,
            aggregateContainerGroup,
            async container =>
            {
                var types = _registeredEventTypes.RegisteredTypes;
                var options = new QueryRequestOptions
                {
                    MaxConcurrency = -1,
                    MaxItemCount = -1,
                    MaxBufferedItemCount = -1
                };
                options.PartitionKey = new PartitionKey(
                    PartitionKeyGenerator.ForCommand(aggregateId, aggregatePayloadType.GetBaseAggregatePayloadTypeFromAggregate()));

                var query = container.GetItemLinqQueryable<IAggregateDocument>()
                    .Where(b => b.DocumentType == DocumentType.Command && b.AggregateId == aggregateId);
                query = sinceSortableUniqueId is not null
                    ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0)
                        .OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var commands = new List<string>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (sinceSortableUniqueId is not null &&
                            new SortableUniqueIdValue(item.SortableUniqueId).EarlierThan(sinceSortableUniqueId))
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
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions
                {
                    MaxConcurrency = -1,
                    MaxItemCount = -1,
                    MaxBufferedItemCount = -1
                };
                var eventTypes = _registeredEventTypes.RegisteredTypes.Select(m => m.Name);
                var query = container.GetItemLinqQueryable<IEvent>()
                    .Where(b => b.DocumentType == DocumentType.Event && b.AggregateType == aggregatePayloadType.Name);

                query = sinceSortableUniqueId is not null
                    ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0)
                        .OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderByDescending(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                var events = new List<IEvent>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string
                            typeName)
                        {
                            continue;
                        }

                        var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                                .Select(
                                    m =>
                                        SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
                                .FirstOrDefault(m => m is not null) ??
                            EventHelper.GetUnregisteredEvent(item);
                        if (toAdd is null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                            toAdd.GetSortableUniqueId().EarlierThan(sinceSortableUniqueId))
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
                    PartitionKey =
                        new PartitionKey(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregatePayloadType, projectionPayloadType))
                };
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId &&
                            b.SavedVersion == version &&
                            b.DocumentTypeName == projectionPayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator =
                    container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    foreach (var obj in await feedIterator.ReadNextAsync())
                    {
                        return true;
                    }
                }
                return false;
            });
    }
}

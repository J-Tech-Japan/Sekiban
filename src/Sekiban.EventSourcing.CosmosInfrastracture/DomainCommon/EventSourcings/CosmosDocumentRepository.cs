using Microsoft.Azure.Cosmos.Linq;
using Sekiban.EventSourcing.Partitions.AggregateIdPartitions;
using Sekiban.EventSourcing.Settings;

namespace CosmosInfrastructure.DomainCommon.EventSourcings;

public class CosmosDocumentRepository : IDocumentPersistentRepository
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly RegisteredEventTypes _registeredEventTypes;
    private readonly ISekibanContext _sekibanContext;
    public CosmosDocumentRepository(CosmosDbFactory cosmosDbFactory, RegisteredEventTypes registeredEventTypes, ISekibanContext sekibanContext)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _registeredEventTypes = registeredEventTypes;
        _sekibanContext = sekibanContext;
    }

    public async Task GetAllAggregateEventsAsync(
        Type multipleProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multipleProjectionType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateEvent,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions();
                var query = targetAggregateNames.Count switch
                {
                    0 => container.GetItemLinqQueryable<IAggregateEvent>()
                        .Where(b => b.DocumentType == DocumentType.AggregateEvent)
                        .OrderByDescending(m => m.SortableUniqueId),
                    _ => container.GetItemLinqQueryable<IAggregateEvent>()
                        .Where(
                            b => b.DocumentType == DocumentType.AggregateEvent &&
                                (targetAggregateNames.Count == 0 || targetAggregateNames.Contains(b.AggregateType)))
                        .OrderByDescending(m => m.SortableUniqueId)
                };
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    var events = new List<IAggregateEvent>();
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (Sekiban.EventSourcing.Shared.SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                            continue;

                        var baseType = typeof(AggregateEvent<>);
                        var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                            .Select(m => Sekiban.EventSourcing.Shared.SekibanJsonHelper.ConvertTo(m, baseType.MakeGenericType(m)) as IAggregateEvent)
                            .FirstOrDefault(m => m != null);
                        if (toAdd == null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.SortableUniqueId == sinceSortableUniqueId)
                        {
                            resultAction(events.OrderBy(m => m.SortableUniqueId));
                            return;
                        }

                        events.Add(toAdd);
                    }
                    resultAction(events.OrderBy(m => m.SortableUniqueId));
                }
            });
    }
    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var partitionKeyFactory = new AggregateIdPartitionKeyFactory(aggregateId, originalType);
                var partitionKey = partitionKeyFactory.GetPartitionKey(DocumentType.AggregateSnapshot);
                var options = new QueryRequestOptions();
                options.PartitionKey = new PartitionKey(partitionKey);
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(b => b.DocumentType == DocumentType.AggregateSnapshot && b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
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
    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type originalType, string partitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var response = await container.ReadItemAsync<SnapshotDocument>(id.ToString(), new PartitionKey(partitionKey));
                return response.Resource;
            });
    }
    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type originalType)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var list = new List<SnapshotDocument>();
                var partitionKeyFactory = new AggregateIdPartitionKeyFactory(aggregateId, originalType);
                var partitionKey = partitionKeyFactory.GetPartitionKey(DocumentType.AggregateSnapshot);
                var options = new QueryRequestOptions();
                options.PartitionKey = new PartitionKey(partitionKey);
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(b => b.DocumentType == DocumentType.AggregateSnapshot && b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
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
    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateEvent,
            aggregateContainerGroup,
            async container =>
            {
                var types = _registeredEventTypes.RegisteredTypes;
                var options = new QueryRequestOptions();
                if (partitionKey != null)
                {
                    options.PartitionKey = new PartitionKey(partitionKey);
                }

                var query = container.GetItemLinqQueryable<IAggregateEvent>()
                    .Where(b => b.DocumentType == DocumentType.AggregateEvent && b.AggregateId == aggregateId);
                query = sinceSortableUniqueId != null ? query.OrderByDescending(m => m.SortableUniqueId) : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    var events = new List<IAggregateEvent>();
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (Sekiban.EventSourcing.Shared.SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                            continue;

                        var baseType = typeof(AggregateEvent<>);
                        var toAdd = types.Where(m => m.Name == typeName)
                            .Select(m => Sekiban.EventSourcing.Shared.SekibanJsonHelper.ConvertTo(m, baseType.MakeGenericType(m)) as IAggregateEvent)
                            .FirstOrDefault(m => m != null);
                        if (toAdd == null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.SortableUniqueId == sinceSortableUniqueId)
                        {
                            resultAction(events.OrderBy(m => m.SortableUniqueId));
                            return;
                        }
                        events.Add(toAdd);
                    }
                    resultAction(events.OrderBy(m => m.SortableUniqueId));
                }
            });
    }
    public async Task GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IAggregateEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateEvent,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions();
                var eventTypes = _registeredEventTypes.RegisteredTypes.Select(m => m.Name);
                var query = container.GetItemLinqQueryable<IAggregateEvent>()
                    .Where(b => b.DocumentType == DocumentType.AggregateEvent && b.AggregateType == originalType.Name)
                    .OrderByDescending(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                while (feedIterator.HasMoreResults)
                {
                    var events = new List<IAggregateEvent>();
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (Sekiban.EventSourcing.Shared.SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                            continue;

                        var baseType = typeof(AggregateEvent<>);
                        var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                            .Select(m => Sekiban.EventSourcing.Shared.SekibanJsonHelper.ConvertTo(m, baseType.MakeGenericType(m)) as IAggregateEvent)
                            .FirstOrDefault(m => m != null);
                        if (toAdd == null)
                        {
                            throw new SekibanUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.SortableUniqueId == sinceSortableUniqueId)
                        {
                            resultAction(events.OrderBy(m => m.SortableUniqueId));
                            return;
                        }

                        events.Add(toAdd);
                    }
                    resultAction(events.OrderBy(m => m.SortableUniqueId));
                }
            });
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var partitionKeyFactory = new AggregateIdPartitionKeyFactory(aggregateId, originalType);
                var partitionKey = partitionKeyFactory.GetPartitionKey(DocumentType.AggregateSnapshot);
                var options = new QueryRequestOptions();
                options.PartitionKey = new PartitionKey(partitionKey);
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(b => b.DocumentType == DocumentType.AggregateSnapshot && b.AggregateId == aggregateId && b.SavedVersion == version)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(query.ToQueryDefinition(), null, options);
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

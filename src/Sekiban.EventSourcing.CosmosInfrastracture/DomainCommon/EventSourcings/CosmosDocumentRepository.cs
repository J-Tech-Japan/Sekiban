using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json.Linq;
using Sekiban.EventSourcing.Partitions.AggregateIdPartitions;
namespace CosmosInfrastructure.DomainCommon.EventSourcings;

public class CosmosDocumentRepository : IDocumentPersistentRepository
{
    private readonly CosmosDbFactory _cosmosDbFactory;
    private readonly RegisteredEventTypes _registeredEventTypes;
    public CosmosDocumentRepository(
        CosmosDbFactory cosmosDbFactory,
        RegisteredEventTypes registeredEventTypes)
    {
        _cosmosDbFactory = cosmosDbFactory;
        _registeredEventTypes = registeredEventTypes;
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(
        Guid aggregateId,
        Type originalType)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var partitionKeyFactory =
                    new AggregateIdPartitionKeyFactory(aggregateId, originalType);
                var partitionKey =
                    partitionKeyFactory.GetPartitionKey(DocumentType.AggregateSnapshot);
                var options = new QueryRequestOptions();
                options.PartitionKey = new PartitionKey(partitionKey);
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotDocument>(
                    query.ToQueryDefinition(),
                    null,
                    options);
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
    public async Task<SnapshotListDocument?> GetLatestSnapshotListForTypeAsync<T>(
        string? partitionKey,
        QueryListType queryListType = QueryListType.ActiveAndDeleted)
        where T : IAggregate
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(typeof(T));

        var aggregateName = typeof(T).Name;
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.SnapshotList,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions();
                if (partitionKey != null)
                {
                    options.PartitionKey = new PartitionKey(partitionKey);
                }
                var query = container.GetItemLinqQueryable<SnapshotListDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.SnapshotList &&
                            b.DocumentTypeName == aggregateName)
                    .OrderByDescending(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<SnapshotListDocument>(
                    query.ToQueryDefinition(),
                    null,
                    options);
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
    public async Task<SnapshotListChunkDocument?> GetSnapshotListChunkByIdAsync(
        Guid id,
        string partitionKey)
    {
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.SnapshotListChunk,
            AggregateContainerGroup.Default,
            async container =>
            {
                var response = await container.ReadItemAsync<SnapshotListChunkDocument>(
                    id.ToString(),
                    new PartitionKey(partitionKey));
                return response.Resource;
            });
    }
    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(
        Guid id,
        Type originalType,
        string partitionKey)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var response = await container.ReadItemAsync<SnapshotDocument>(
                    id.ToString(),
                    new PartitionKey(partitionKey));
                return response.Resource;
            });
    }
    public async Task GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

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

                var query = container.GetItemLinqQueryable<AggregateEvent>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateEvent &&
                            b.AggregateId == aggregateId);
                query = sinceSortableUniqueId != null
                    ? query.OrderByDescending(m => m.SortableUniqueId)
                    : query.OrderBy(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
                    var events = new List<AggregateEvent>();
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (item is not JObject jobj)
                        {
                            continue;
                        }
                        var typeName = jobj.GetValue(nameof(Document.DocumentTypeName))?.ToString();
                        var toAdd = types.Where(m => m.Name == typeName)
                            .Select(m => (AggregateEvent?)jobj.ToObject(m))
                            .FirstOrDefault(m => m != null);
                        if (toAdd == null)
                        {
                            throw new JJUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                            toAdd.SortableUniqueId == sinceSortableUniqueId)
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
        Action<IEnumerable<AggregateEvent>> resultAction)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

        await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateEvent,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions();
                var eventTypes = _registeredEventTypes.RegisteredTypes.Select(m => m.Name);
                var query = container.GetItemLinqQueryable<AggregateEvent>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateEvent &&
                            b.AggregateType == originalType.Name)
                    .OrderByDescending(m => m.SortableUniqueId);
                var feedIterator = container.GetItemQueryIterator<dynamic>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
                    var events = new List<AggregateEvent>();
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // pick out one album
                        if (item is not JObject jobj) { continue; }
                        var typeName = jobj.GetValue(nameof(Document.DocumentTypeName))?.ToString();
                        if (typeName == null)
                        {
                            continue;
                        }

                        var toAdd = _registeredEventTypes.RegisteredTypes
                            .Where(m => m.Name == typeName)
                            .Select(m => (AggregateEvent?)jobj.ToObject(m))
                            .FirstOrDefault(m => m != null);
                        if (toAdd == null)
                        {
                            throw new JJUnregisterdEventFoundException();
                        }

                        if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) &&
                            toAdd.SortableUniqueId == sinceSortableUniqueId)
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
}

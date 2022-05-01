using Microsoft.Azure.Cosmos.Linq;
using Newtonsoft.Json.Linq;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Queries;
using Sekiban.EventSourcing.Shared.Exceptions;
using Sekiban.EventSourcing.Snapshots;
namespace CosmosInfrastructure.DomainCommon.EventSourcings;

public class CosmosDocumentRepository : IDocumentRepository
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
        Type originalType,
        string? partitionKey)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
        return await _cosmosDbFactory.CosmosActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions();
                if (partitionKey != null)
                {
                    options.PartitionKey = new PartitionKey(partitionKey);
                }
                var query = container.GetItemLinqQueryable<SnapshotDocument>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateSnapshot &&
                            b.AggregateId == aggregateId)
                    .OrderByDescending(m => m.Ts);
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
                    .OrderByDescending(m => m.Ts);
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
    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Type originalType, string partitionKey)
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

    public async Task<IEnumerable<AggregateEvent>> GetAllAggregateEventsForAggregateIdAsync(
        Guid aggregateId,
        Type originalType,
        string? partitionKey = null,
        Guid? sinceEventId = null)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

        return await _cosmosDbFactory.CosmosActionAsync<IEnumerable<AggregateEvent>>(
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

                var events = new List<AggregateEvent>();
                var query = container.GetItemLinqQueryable<AggregateEvent>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateEvent &&
                            b.AggregateId == aggregateId);
                query = sinceEventId.HasValue ? query.OrderByDescending(m => m.Ts)
                    : query.OrderBy(m => m.Ts);
                var feedIterator = container.GetItemQueryIterator<dynamic>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
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

                        if (sinceEventId.HasValue && toAdd.Id == sinceEventId.Value)
                        {
                            return events.OrderBy(m => m.Ts);
                        }

                        events.Add(toAdd);
                    }
                }
                return events.OrderBy(m => m.Ts);
            });
    }
    public async Task<IEnumerable<AggregateEvent>> GetAllAggregateEventsForAggregateEventTypeAsync(
        Type originalType,
        Guid? sinceEventId = null)
    {
        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

        return await _cosmosDbFactory.CosmosActionAsync<IEnumerable<AggregateEvent>>(
            DocumentType.AggregateEvent,
            aggregateContainerGroup,
            async container =>
            {
                var options = new QueryRequestOptions();
                var events = new List<AggregateEvent>();
                var eventTypes = _registeredEventTypes.RegisteredTypes.Select(m => m.Name);
                var query = container.GetItemLinqQueryable<AggregateEvent>()
                    .Where(
                        b => b.DocumentType == DocumentType.AggregateEvent &&
                            b.AggregateType == originalType.Name)
                    .OrderByDescending(m => m.Ts);
                var feedIterator = container.GetItemQueryIterator<dynamic>(
                    query.ToQueryDefinition(),
                    null,
                    options);
                while (feedIterator.HasMoreResults)
                {
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

                        if (sinceEventId.HasValue &&
                            toAdd.Id == sinceEventId.Value)
                        {
                            return events.OrderBy(m => m.Ts);
                        }

                        events.Add(toAdd);
                    }
                }
                return events.OrderBy(m => m.Ts);
            });
    }
}

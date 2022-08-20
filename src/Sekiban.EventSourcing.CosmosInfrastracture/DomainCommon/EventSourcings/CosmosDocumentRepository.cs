using Microsoft.Azure.Cosmos.Linq;
using Sekiban.EventSourcing.Documents.ValueObjects;
using Sekiban.EventSourcing.Partitions;
using Sekiban.EventSourcing.Settings;
using Sekiban.EventSourcing.Shared;
namespace CosmosInfrastructure.DomainCommon.EventSourcings
{
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
                        0 => container.GetItemLinqQueryable<IAggregateEvent>().Where(b => b.DocumentType == DocumentType.AggregateEvent),
                        _ => container.GetItemLinqQueryable<IAggregateEvent>()
                            .Where(
                                b => b.DocumentType == DocumentType.AggregateEvent &&
                                    (targetAggregateNames.Count == 0 || targetAggregateNames.Contains(b.AggregateType)))
                    };
                    if (!string.IsNullOrEmpty(sinceSortableUniqueId))
                    {
                        query = query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0);
                    }

                    query = query.OrderByDescending(m => m.SortableUniqueId);
                    var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                    var events = new List<IAggregateEvent>();
                    while (feedIterator.HasMoreResults)
                    {
                        var response = await feedIterator.ReadNextAsync();
                        foreach (var item in response)
                        {
                            // pick out one item
                            if (SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.DocumentTypeName)) is not string typeName)
                            {
                                continue;
                            }

                            var toAdd = _registeredEventTypes.RegisteredTypes.Where(m => m.Name == typeName)
                                .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(AggregateEvent<>).MakeGenericType(m)) as IAggregateEvent)
                                .FirstOrDefault(m => m is not null);
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
        public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type originalType)
        {
            var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
            return await _cosmosDbFactory.CosmosActionAsync(
                DocumentType.AggregateSnapshot,
                aggregateContainerGroup,
                async container =>
                {
                    var options = new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, originalType))
                    };
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
                    var options = new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, originalType))
                    };
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
                    if (partitionKey is not null)
                    {
                        options.PartitionKey = new PartitionKey(partitionKey);
                    }

                    var query = container.GetItemLinqQueryable<IAggregateEvent>()
                        .Where(b => b.DocumentType == DocumentType.AggregateEvent && b.AggregateId == aggregateId);
                    query = sinceSortableUniqueId is not null
                        ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0).OrderByDescending(m => m.SortableUniqueId)
                        : query.OrderBy(m => m.SortableUniqueId);
                    var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                    var events = new List<IAggregateEvent>();
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
                                .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(AggregateEvent<>).MakeGenericType(m)) as IAggregateEvent)
                                .FirstOrDefault(m => m is not null);
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
        public async Task GetAllAggregateEventStringsForAggregateIdAsync(
            Guid aggregateId,
            Type originalType,
            string? partitionKey,
            string? sinceSortableUniqueId,
            Action<IEnumerable<string>> resultAction) =>
            await GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                originalType,
                partitionKey,
                sinceSortableUniqueId,
                events =>
                {
                    resultAction(events.Select(SekibanJsonHelper.Serialize).Where(m => !string.IsNullOrEmpty(m))!);
                });

        public async Task GetAllAggregateCommandStringsForAggregateIdAsync(
            Guid aggregateId,
            Type originalType,
            string? sinceSortableUniqueId,
            Action<IEnumerable<string>> resultAction)
        {
            var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);

            await _cosmosDbFactory.CosmosActionAsync(
                DocumentType.AggregateCommand,
                aggregateContainerGroup,
                async container =>
                {
                    var types = _registeredEventTypes.RegisteredTypes;
                    var options = new QueryRequestOptions();
                    options.PartitionKey = new PartitionKey(PartitionKeyGenerator.ForAggregateCommand(aggregateId, originalType));

                    var query = container.GetItemLinqQueryable<IDocument>()
                        .Where(b => b.DocumentType == DocumentType.AggregateCommand && b.AggregateId == aggregateId);
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
                        .Where(b => b.DocumentType == DocumentType.AggregateEvent && b.AggregateType == originalType.Name);

                    query = sinceSortableUniqueId is not null
                        ? query.Where(m => m.SortableUniqueId.CompareTo(sinceSortableUniqueId) > 0).OrderByDescending(m => m.SortableUniqueId)
                        : query.OrderByDescending(m => m.SortableUniqueId);
                    var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition(), null, options);
                    var events = new List<IAggregateEvent>();
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
                                .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(AggregateEvent<>).MakeGenericType(m)) as IAggregateEvent)
                                .FirstOrDefault(m => m is not null);
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

        public async Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type originalType, int version)
        {
            var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(originalType);
            return await _cosmosDbFactory.CosmosActionAsync(
                DocumentType.AggregateSnapshot,
                aggregateContainerGroup,
                async container =>
                {
                    var options = new QueryRequestOptions
                    {
                        PartitionKey = new PartitionKey(PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, originalType))
                    };
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
}

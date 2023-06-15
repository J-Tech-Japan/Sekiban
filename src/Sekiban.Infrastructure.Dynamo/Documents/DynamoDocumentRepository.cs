using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
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
using Sekiban.Core.Types;
using System.Text.Json;
using Document = Sekiban.Core.Documents.Document;
namespace Sekiban.Infrastructure.Dynamo.Documents;

public class DynamoDocumentRepository : IDocumentPersistentRepository
{
    private readonly DynamoDbFactory _dbFactory;
    private readonly RegisteredEventTypes _registeredEventTypes;
    private readonly ISingleProjectionSnapshotAccessor _singleProjectionSnapshotAccessor;

    public DynamoDocumentRepository(
        DynamoDbFactory dbFactory,
        RegisteredEventTypes registeredEventTypes,
        ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor)
    {
        _dbFactory = dbFactory;
        _registeredEventTypes = registeredEventTypes;
        _singleProjectionSnapshotAccessor = singleProjectionSnapshotAccessor;
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

        await _dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async table =>
            {
                var types = _registeredEventTypes.RegisteredTypes;

                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                if (sinceSortableUniqueId is not null)
                {
                    filter.AddCondition(nameof(Document.SortableUniqueId), QueryOperator.GreaterThan, sinceSortableUniqueId);
                }
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = false };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                } while (!search.IsDone);
                var events = new List<IEvent>();
                foreach (var document in resultList)
                {
                    var json = document.ToJson();
                    var jsonElement = JsonDocument.Parse(json).RootElement;
                    var documentTypeName = document[nameof(IDocument.DocumentTypeName)].AsString();
                    if (documentTypeName is null) { continue; }
                    var toAdd = types.Where(m => m.Name == documentTypeName)
                            .Select(m => SekibanJsonHelper.Deserialize(json, typeof(Event<>).MakeGenericType(m)) as IEvent)
                            .FirstOrDefault(m => m is not null) ??
                        EventHelper.GetUnregisteredEvent(jsonElement);
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
    public async Task GetAllCommandStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _dbFactory.DynamoActionAsync(
            DocumentType.Command,
            aggregateContainerGroup,
            async table =>
            {
                var partitionKey = PartitionKeyGenerator.ForCommand(
                    aggregateId,
                    aggregatePayloadType.GetBaseAggregatePayloadTypeFromAggregate(),
                    rootPartitionKey);
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                if (sinceSortableUniqueId is not null)
                {
                    filter.AddCondition(nameof(Document.SortableUniqueId), QueryOperator.GreaterThan, sinceSortableUniqueId);
                }
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = false };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                } while (!search.IsDone);
                var commands = (from document in resultList
                                let json = document.ToJson()
                                let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                where sinceSortableUniqueId is null || !new SortableUniqueIdValue(sortableUniqueId).EarlierThan(sinceSortableUniqueId)
                                select json).ToList();
                resultAction(commands);
            });

    }
    public async Task GetAllEventsForAggregateAsync(
        Type aggregatePayloadType,
        string? sinceSortableUniqueId,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await _dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async table =>
            {
                var types = _registeredEventTypes.RegisteredTypes;

                var filter = new QueryFilter();
                filter.AddCondition(nameof(IEvent.AggregateType), QueryOperator.Equal, aggregatePayloadType.Name);
                if (sinceSortableUniqueId is not null)
                {
                    filter.AddCondition(nameof(Document.SortableUniqueId), QueryOperator.GreaterThan, sinceSortableUniqueId);
                }
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                } while (!search.IsDone);
                var events = new List<IEvent>();
                foreach (var document in resultList)
                {
                    var json = document.ToJson();
                    var jsonElement = JsonDocument.Parse(json).RootElement;
                    var documentTypeName = document[nameof(IDocument.DocumentTypeName)].AsString();
                    if (documentTypeName is null) { continue; }
                    var toAdd = types.Where(m => m.Name == documentTypeName)
                            .Select(m => SekibanJsonHelper.Deserialize(json, typeof(Event<>).MakeGenericType(m)) as IEvent)
                            .FirstOrDefault(m => m is not null) ??
                        EventHelper.GetUnregisteredEvent(jsonElement);
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
            });
    }
    public async Task GetAllEventsAsync(
        Type multiProjectionType,
        IList<string> targetAggregateNames,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionType);

        await _dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async table =>
            {
                var types = _registeredEventTypes.RegisteredTypes;

                var filter = new ScanFilter();
                filter.AddCondition(nameof(IEvent.AggregateType), ScanOperator.In, targetAggregateNames.Select(m => new AttributeValue(m)).ToList());
                if (rootPartitionKey.Equals(IMultiProjectionService.ProjectionAllPartitions))
                {
                    filter.AddCondition(nameof(IDocument.RootPartitionKey), ScanOperator.Equal, rootPartitionKey);
                }
                if (sinceSortableUniqueId is not null)
                {
                    filter.AddCondition(nameof(Document.SortableUniqueId), ScanOperator.GreaterThan, sinceSortableUniqueId);
                }
                var config = new ScanOperationConfig { Filter = filter };
                var search = table.Scan(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                } while (!search.IsDone);
                var events = new List<IEvent>();
                foreach (var document in resultList)
                {
                    var json = document.ToJson();
                    var jsonElement = JsonDocument.Parse(json).RootElement;
                    var documentTypeName = document[nameof(IDocument.DocumentTypeName)].AsString();
                    if (documentTypeName is null) { continue; }
                    var toAdd = types.Where(m => m.Name == documentTypeName)
                            .Select(m => SekibanJsonHelper.Deserialize(json, typeof(Event<>).MakeGenericType(m)) as IEvent)
                            .FirstOrDefault(m => m is not null) ??
                        EventHelper.GetUnregisteredEvent(jsonElement);
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
        return await _dbFactory.DynamoActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async table =>
            {
                var partitionKey = PartitionKeyGenerator.ForAggregateSnapshot(
                    aggregateId,
                    aggregatePayloadType,
                    projectionPayloadType,
                    rootPartitionKey);
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                filter.AddCondition(nameof(SnapshotDocument.PayloadVersionIdentifier), QueryOperator.Equal, payloadVersionIdentifier);

                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                    if (resultList.Any()) { break; }
                } while (!search.IsDone);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                var snapshotJson = snapshots.FirstOrDefault();
                if (string.IsNullOrEmpty(snapshotJson)) { return null; }
                var snapshot = SekibanJsonHelper.Deserialize<SnapshotDocument>(snapshotJson);
                if (snapshot is null) { return null; }
                return await _singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
            });
    }
    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return await _dbFactory.DynamoActionAsync(
            DocumentType.MultiProjectionSnapshot,
            aggregateContainerGroup,
            async table =>
            {
                var partitionKey = PartitionKeyGenerator.ForMultiProjectionSnapshot(multiProjectionPayloadType, rootPartitionKey);
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                filter.AddCondition(nameof(MultiProjectionSnapshotDocument.PayloadVersionIdentifier), QueryOperator.Equal, payloadVersionIdentifier);

                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                    if (resultList.Any()) { break; }
                } while (!search.IsDone);

                foreach (var result in resultList)
                {

                    var snapshot = SekibanJsonHelper.Deserialize<MultiProjectionSnapshotDocument>(result.ToJson());
                    if (snapshot is null) { continue; }
                    return snapshot;
                }
                return null;
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
        return await _dbFactory.DynamoActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async table =>
            {
                var partitionKey = PartitionKeyGenerator.ForAggregateSnapshot(
                    aggregateId,
                    aggregatePayloadType,
                    projectionPayloadType,
                    rootPartitionKey);
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                filter.AddCondition(nameof(SnapshotDocument.PayloadVersionIdentifier), QueryOperator.Equal, payloadVersionIdentifier);
                filter.AddCondition(nameof(SnapshotDocument.SavedVersion), QueryOperator.Equal, version);

                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                    if (resultList.Any()) { break; }
                } while (!search.IsDone);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                var snapshotJson = snapshots.FirstOrDefault();
                if (string.IsNullOrEmpty(snapshotJson)) { return false; }
                var snapshot = SekibanJsonHelper.Deserialize<SnapshotDocument>(snapshotJson);
                if (snapshot is null) { return false; }

                return true;
            });
    }
    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await _dbFactory.DynamoActionAsync<List<SnapshotDocument>>(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async table =>
            {
                var partitionKey = PartitionKeyGenerator.ForAggregateSnapshot(
                    aggregateId,
                    aggregatePayloadType,
                    projectionPayloadType,
                    rootPartitionKey);
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                } while (!search.IsDone);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                if (!snapshots.Any()) { return new List<SnapshotDocument>(); }
                var snapshotDocuments = snapshots.Select(m => SekibanJsonHelper.Deserialize<SnapshotDocument>(m)).ToList();
                if (!snapshotDocuments.Any()) { return new List<SnapshotDocument>(); }
                var toReturn = new List<SnapshotDocument>();
                foreach (var snapshotDocument in snapshotDocuments)
                {
                    if (snapshotDocument is null) { continue; }
                    var filledSnapshot = await _singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshotDocument);
                    if (filledSnapshot is null) { continue; }
                    toReturn.Add(filledSnapshot);
                }
                return toReturn;
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
        return await _dbFactory.DynamoActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async table =>
            {
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                filter.AddCondition(nameof(SnapshotDocument.Id), QueryOperator.Equal, id);
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = new List<Amazon.DynamoDBv2.DocumentModel.Document>();
                do
                {
                    if (search is null)
                    {
                        break;
                    }
                    var nextSet = await search.GetNextSetAsync();
                    resultList.AddRange(nextSet);
                    if (resultList.Any()) { break; }
                } while (!search.IsDone);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                var snapshotJson = snapshots.FirstOrDefault();
                if (string.IsNullOrEmpty(snapshotJson)) { return null; }
                var snapshot = SekibanJsonHelper.Deserialize<SnapshotDocument>(snapshotJson);
                if (snapshot is null) { return null; }
                return await _singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
            });
    }
}

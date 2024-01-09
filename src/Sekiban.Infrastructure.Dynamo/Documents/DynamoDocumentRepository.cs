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

/// <summary>
///     Receive data from DynamoDB
/// </summary>
public class DynamoDocumentRepository(
    DynamoDbFactory dbFactory,
    RegisteredEventTypes registeredEventTypes,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor) : IDocumentPersistentRepository
{
    public async Task GetAllEventsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

        await dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async table =>
            {
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                filter.AddSortableUniqueIdIfNull(sinceSortableUniqueId);
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = false };
                var search = table.Query(config);

                var resultList = await FetchDocumentsAsync(search);
                var events = ProcessEventDocuments(resultList, sinceSortableUniqueId);
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

        await dbFactory.DynamoActionAsync(
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
                filter.AddSortableUniqueIdIfNull(sinceSortableUniqueId);
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = false };
                var search = table.Query(config);

                var resultList = await FetchDocumentsAsync(search);
                var commands = (from document in resultList
                                let json = document.ToJson()
                                let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                where sinceSortableUniqueId is null ||
                                    !new SortableUniqueIdValue(sortableUniqueId).IsEarlierThan(sinceSortableUniqueId)
                                select json).ToList();
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

        await dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async table =>
            {
                var filter = new QueryFilter();
                filter.AddCondition(nameof(IEvent.AggregateType), QueryOperator.Equal, aggregatePayloadType.Name);
                if (rootPartitionKey != IMultiProjectionService.ProjectionAllRootPartitions)
                {
                    filter.AddCondition(nameof(Document.RootPartitionKey), QueryOperator.Equal, rootPartitionKey);
                }
                filter.AddSortableUniqueIdIfNull(sinceSortableUniqueId);
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = await FetchDocumentsAsync(search);
                var events = ProcessEventDocuments(resultList, sinceSortableUniqueId);
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

        await dbFactory.DynamoActionAsync(
            DocumentType.Event,
            aggregateContainerGroup,
            async table =>
            {
                var filter = new ScanFilter();
                if (targetAggregateNames.Count > 0)
                {
                    filter.AddCondition(
                        nameof(IEvent.AggregateType),
                        ScanOperator.In,
                        targetAggregateNames.Select(m => new AttributeValue(m)).ToList());
                }
                if (!rootPartitionKey.Equals(IMultiProjectionService.ProjectionAllRootPartitions))
                {
                    filter.AddCondition(nameof(IDocument.RootPartitionKey), ScanOperator.Equal, rootPartitionKey);
                }
                filter.AddSortableUniqueIdIfNull(sinceSortableUniqueId);
                var config = new ScanOperationConfig { Filter = filter };
                var search = table.Scan(config);

                var resultList = await FetchDocumentsAsync(search);
                var events = ProcessEventDocuments(resultList, sinceSortableUniqueId);
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
        return await dbFactory.DynamoActionAsync(
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

                var resultList = await FetchDocumentsAsync(search);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                var snapshotJson = snapshots.FirstOrDefault();
                if (string.IsNullOrEmpty(snapshotJson)) { return null; }
                var snapshot = SekibanJsonHelper.Deserialize<SnapshotDocument>(snapshotJson);
                return snapshot is null ? null : await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
            });
    }

    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return await dbFactory.DynamoActionAsync(
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

                var resultList = await FetchDocumentsAsync(search);

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
        return await dbFactory.DynamoActionAsync(
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

                var resultList = await FetchDocumentsAsync(search);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                var snapshotJson = snapshots.FirstOrDefault();
                if (string.IsNullOrEmpty(snapshotJson)) { return false; }
                var snapshot = SekibanJsonHelper.Deserialize<SnapshotDocument>(snapshotJson);
                return snapshot is not null;
            });
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        Type projectionPayloadType,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        return await dbFactory.DynamoActionAsync<List<SnapshotDocument>>(
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

                var resultList = await FetchDocumentsAsync(search);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                if (snapshots.Count == 0) { return []; }
                var snapshotDocuments = snapshots.Select(m => SekibanJsonHelper.Deserialize<SnapshotDocument>(m)).ToList();
                if (snapshotDocuments.Count == 0) { return []; }
                var toReturn = new List<SnapshotDocument>();
                foreach (var snapshotDocument in snapshotDocuments)
                {
                    if (snapshotDocument is null) { continue; }
                    var filledSnapshot = await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshotDocument);
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
        return await dbFactory.DynamoActionAsync(
            DocumentType.AggregateSnapshot,
            aggregateContainerGroup,
            async table =>
            {
                var filter = new QueryFilter();
                filter.AddCondition(nameof(Document.PartitionKey), QueryOperator.Equal, partitionKey);
                filter.AddCondition(nameof(SnapshotDocument.Id), QueryOperator.Equal, id);
                var config = new QueryOperationConfig { Filter = filter, BackwardSearch = true };
                var search = table.Query(config);

                var resultList = await FetchDocumentsAsync(search);
                var snapshots = (from document in resultList
                                 let json = document.ToJson()
                                 let sortableUniqueId = document[nameof(IDocument.SortableUniqueId)].AsString()
                                 select json).ToList();
                var snapshotJson = snapshots.FirstOrDefault();
                if (string.IsNullOrEmpty(snapshotJson)) { return null; }
                var snapshot = SekibanJsonHelper.Deserialize<SnapshotDocument>(snapshotJson);
                return snapshot is null ? null : await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
            });
    }

    private static async Task<List<Amazon.DynamoDBv2.DocumentModel.Document>> FetchDocumentsAsync(Search? search)
    {
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
        return resultList;
    }

    private List<IEvent> ProcessEventDocuments(List<Amazon.DynamoDBv2.DocumentModel.Document> documents, string? sinceSortableUniqueId)
    {
        var types = registeredEventTypes.RegisteredTypes;
        var events = new List<IEvent>();
        foreach (var document in documents)
        {
            var json = document.ToJson();
            var jsonElement = JsonDocument.Parse(json).RootElement;
            var documentTypeName = document[nameof(IDocument.DocumentTypeName)].AsString();
            if (documentTypeName is null) { continue; }
            var toAdd = (types.Where(m => m.Name == documentTypeName)
                        .Select(m => SekibanJsonHelper.Deserialize(json, typeof(Event<>).MakeGenericType(m)) as IEvent)
                        .FirstOrDefault(m => m is not null) ??
                    EventHelper.GetUnregisteredEvent(jsonElement)) ??
                throw new SekibanUnregisteredEventFoundException();
            if (!string.IsNullOrWhiteSpace(sinceSortableUniqueId) && toAdd.GetSortableUniqueId().IsEarlierThan(sinceSortableUniqueId))
            {
                continue;
            }
            events.Add(toAdd);
        }
        return events;
    }
}

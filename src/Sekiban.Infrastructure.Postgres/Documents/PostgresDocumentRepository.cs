using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.Postgres.Databases;
using System.Text.Json;
namespace Sekiban.Infrastructure.Postgres.Documents;

public class PostgresDocumentRepository(
    PostgresDbFactory dbFactory,
    RegisteredEventTypes registeredEventTypes,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor) : IDocumentPersistentRepository
{
    public async Task<ResultBox<UnitValue>> GetEvents(
        EventRetrievalInfo eventRetrievalInfo,
        Action<IEnumerable<IEvent>> resultAction)
    {
        var aggregateContainerGroup = eventRetrievalInfo.GetAggregateContainerGroup();
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                if (eventRetrievalInfo.GetIsPartition())
                {
                    var partitionKey = eventRetrievalInfo.GetPartitionKey().UnwrapBox();
                    switch (aggregateContainerGroup)
                    {
                        case AggregateContainerGroup.Default:

                            var query = dbContext.Events.Where(m => m.PartitionKey == partitionKey) ??
                                throw new SekibanInvalidArgumentException();
                            query = eventRetrievalInfo.SinceSortableUniqueId.HasValue
                                ? query
                                    .Where(
                                        m => string.Compare(
                                                m.SortableUniqueId,
                                                eventRetrievalInfo.SinceSortableUniqueId.GetValue().Value) >
                                            0)
                                    .OrderBy(m => m.SortableUniqueId)
                                : query.OrderBy(m => m.SortableUniqueId);
                            // take 1000 events each and run resultAction
                            GetEventsInBatches(query).ForEach(resultAction);
                            break;

                        case AggregateContainerGroup.Dissolvable:
                            var queryDissolvable
                                = dbContext.DissolvableEvents.Where(m => m.PartitionKey == partitionKey) ??
                                throw new SekibanInvalidArgumentException();
                            queryDissolvable = eventRetrievalInfo.SinceSortableUniqueId.HasValue
                                ? queryDissolvable
                                    .Where(
                                        m => string.Compare(
                                                m.SortableUniqueId,
                                                eventRetrievalInfo.SinceSortableUniqueId.GetValue().Value) >
                                            0)
                                    .OrderBy(m => m.SortableUniqueId)
                                : queryDissolvable.OrderBy(m => m.SortableUniqueId);
                            // take 1000 events each and run resultAction
                            foreach (var ev in GetEventsInBatches(queryDissolvable))
                            {
                                resultAction(ev);
                            }
                            break;
                    }
                } else
                {
                    switch (aggregateContainerGroup)
                    {
                        case AggregateContainerGroup.Default:
                            var query = dbContext.Events.AsQueryable();
                            if (eventRetrievalInfo.HasAggregateStream())
                            {
                                var aggregateNames = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
                                query = query.Where(m => aggregateNames.Contains(m.AggregateType));
                            }
                            if (eventRetrievalInfo.HasRootPartitionKey())
                            {
                                var rootPartitionKey = eventRetrievalInfo.RootPartitionKey.GetValue();
                                query = query.Where(m => m.RootPartitionKey == rootPartitionKey);
                            }
                            query = eventRetrievalInfo.SinceSortableUniqueId.HasValue
                                ? query
                                    .Where(
                                        m => string.Compare(
                                                m.SortableUniqueId,
                                                eventRetrievalInfo.SinceSortableUniqueId.GetValue().Value) >
                                            0)
                                    .OrderBy(m => m.SortableUniqueId)
                                : query.OrderBy(m => m.SortableUniqueId);
                            // take 1000 events each and run resultAction
                            GetEventsInBatches(query).ForEach(resultAction);
                            break;

                        case AggregateContainerGroup.Dissolvable:


                            var queryDissolvable = dbContext.DissolvableEvents.AsQueryable();
                            if (eventRetrievalInfo.HasAggregateStream())
                            {
                                var aggregateNames = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
                                queryDissolvable
                                    = queryDissolvable.Where(m => aggregateNames.Contains(m.AggregateType));
                            }
                            if (eventRetrievalInfo.HasRootPartitionKey())
                            {
                                var rootPartitionKey = eventRetrievalInfo.RootPartitionKey.GetValue();
                                queryDissolvable = queryDissolvable.Where(m => m.RootPartitionKey == rootPartitionKey);
                            }
                            queryDissolvable = eventRetrievalInfo.SinceSortableUniqueId.HasValue
                                ? queryDissolvable
                                    .Where(
                                        m => string.Compare(
                                                m.SortableUniqueId,
                                                eventRetrievalInfo.SinceSortableUniqueId.GetValue().Value) >
                                            0)
                                    .OrderBy(m => m.SortableUniqueId)
                                : queryDissolvable.OrderBy(m => m.SortableUniqueId);
                            // take 1000 events each and run resultAction
                            GetEventsInBatches(queryDissolvable).ForEach(resultAction);
                            break;
                    }
                    await Task.CompletedTask;

                }

            });
        return ResultBox.UnitValue;
    }
    public async Task GetAllEventStringsForAggregateIdAsync(
        Guid aggregateId,
        Type aggregatePayloadType,
        string? partitionKey,
        string? sinceSortableUniqueId,
        string rootPartitionKey,
        Action<IEnumerable<string>> resultAction)
    {
        await GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new AggregateTypeStream(aggregatePayloadType),
                aggregateId,
                sinceSortableUniqueId),
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
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var partition = PartitionKeyGenerator.ForCommand(aggregateId, aggregatePayloadType, rootPartitionKey);
                var query = dbContext.Commands.Where(m => m.AggregateContainerGroup == aggregateContainerGroup);
                query = query.Where(m => m.PartitionKey == partition);
                GetCommandsInBatches(query, sinceSortableUniqueId).ForEach(resultAction);
                await Task.CompletedTask;
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
        return await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var partitionKey = PartitionKeyGenerator.ForAggregateSnapshot(
                    aggregateId,
                    aggregatePayloadType,
                    projectionPayloadType,
                    rootPartitionKey);
                var query = dbContext
                    .SingleProjectionSnapshots
                    .Where(
                        b => b.AggregateContainerGroup == aggregateContainerGroup &&
                            b.PartitionKey == partitionKey &&
                            b.AggregateId == aggregateId &&
                            b.RootPartitionKey == rootPartitionKey &&
                            b.AggregateType == aggregatePayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                foreach (var obj in query)
                {
                    var snapshot = GetSnapshotDocument(obj);
                    if (snapshot is null) { continue; }
                    return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
                }
                return null;
            });
    }
    public async Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(
        Type multiProjectionPayloadType,
        string payloadVersionIdentifier,
        string rootPartitionKey = IMultiProjectionService.ProjectionAllRootPartitions)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(multiProjectionPayloadType);
        return await dbFactory.DbActionAsync(
            dbContext =>
            {
                var partitionKey = PartitionKeyGenerator.ForMultiProjectionSnapshot(
                    multiProjectionPayloadType,
                    rootPartitionKey);
                var query = dbContext
                    .MultiProjectionSnapshots
                    .Where(
                        b => b.AggregateContainerGroup == aggregateContainerGroup &&
                            b.PartitionKey == partitionKey &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                return Task.FromResult(
                    Enumerable
                        .OfType<DbMultiProjectionDocument>(query)
                        .Select(obj => obj.ToMultiProjectionSnapshotDocument())
                        .FirstOrDefault());
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
        return await dbFactory.DbActionAsync(
            dbContext =>
            {
                var partitionKey = PartitionKeyGenerator.ForAggregateSnapshot(
                    aggregateId,
                    aggregatePayloadType,
                    projectionPayloadType,
                    rootPartitionKey);
                var query = dbContext
                    .SingleProjectionSnapshots
                    .Where(
                        b => b.AggregateContainerGroup == aggregateContainerGroup &&
                            b.PartitionKey == partitionKey &&
                            b.AggregateId == aggregateId &&
                            b.RootPartitionKey == rootPartitionKey &&
                            b.AggregateType == aggregatePayloadType.Name &&
                            b.PayloadVersionIdentifier == payloadVersionIdentifier &&
                            b.SavedVersion == version)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                return Task.FromResult(
                    Enumerable
                        .OfType<DbSingleProjectionSnapshotDocument>(query)
                        .Select(obj => GetSnapshotDocument(obj))
                        .OfType<SnapshotDocument>()
                        .Any());
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
        return await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var query = dbContext
                    .SingleProjectionSnapshots
                    .Where(
                        b => b.AggregateContainerGroup == aggregateContainerGroup &&
                            b.PartitionKey == partitionKey &&
                            b.AggregateId == aggregateId &&
                            b.RootPartitionKey == rootPartitionKey &&
                            b.AggregateType == aggregatePayloadType.Name)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                foreach (var obj in query)
                {
                    var snapshot = GetSnapshotDocument(obj);
                    if (snapshot is null) { continue; }
                    return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
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
        return await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var partitionKey = PartitionKeyGenerator.ForAggregateSnapshot(
                    aggregateId,
                    aggregatePayloadType,
                    projectionPayloadType,
                    rootPartitionKey);
                var query = dbContext
                    .SingleProjectionSnapshots
                    .Where(
                        b => b.AggregateContainerGroup == aggregateContainerGroup &&
                            b.PartitionKey == partitionKey &&
                            b.AggregateId == aggregateId &&
                            b.RootPartitionKey == rootPartitionKey &&
                            b.AggregateType == aggregatePayloadType.Name)
                    .OrderByDescending(m => m.LastSortableUniqueId);
                var list = new List<SnapshotDocument>();
                foreach (var obj in query)
                {
                    var snapshot = GetSnapshotDocument(obj);
                    var filled = snapshot is null
                        ? null
                        : await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);
                    if (filled is not null)
                    {
                        list.Add(filled);
                    }
                }
                return list;
            });
    }


    private SnapshotDocument? GetSnapshotDocument(DbSingleProjectionSnapshotDocument dbSnapshot)
    {
        var payload = dbSnapshot.Snapshot is null
            ? null
            : SekibanJsonHelper.Deserialize(dbSnapshot.Snapshot, typeof(JsonElement));
        return new SnapshotDocument
        {
            Id = dbSnapshot.Id,
            AggregateId = dbSnapshot.AggregateId,
            PartitionKey = dbSnapshot.PartitionKey,
            DocumentType = dbSnapshot.DocumentType,
            DocumentTypeName = dbSnapshot.DocumentTypeName,
            TimeStamp = dbSnapshot.TimeStamp,
            SortableUniqueId = dbSnapshot.SortableUniqueId,
            AggregateType = dbSnapshot.AggregateType,
            RootPartitionKey = dbSnapshot.RootPartitionKey,
            Snapshot = payload,
            LastEventId = dbSnapshot.LastEventId,
            LastSortableUniqueId = dbSnapshot.LastSortableUniqueId,
            SavedVersion = dbSnapshot.SavedVersion,
            PayloadVersionIdentifier = dbSnapshot.PayloadVersionIdentifier
        };
    }


    private IEnumerable<IEnumerable<string>> GetCommandsInBatches(
        IEnumerable<DbCommandDocument> commands,
        string? sinceSortableUniqueId)
    {
        const int batchSize = 1000;
        List<string> commandBatch = [];

        foreach (var commandItem in commands)
        {
            var commandDocument = FromDbCommand(commandItem, sinceSortableUniqueId);
            if (commandDocument is not null)
            {
                commandBatch.Add(JsonSerializer.Serialize(commandDocument));
            }
            if (commandBatch.Count >= batchSize)
            {
                yield return commandBatch;
                commandBatch = [];
            }
        }

        if (commandBatch.Any())
        {
            yield return commandBatch;
        }
    }
    private CommandDocumentForJsonExport? FromDbCommand(DbCommandDocument dbCommand, string? sinceSortableUniqueId)
    {
        var payload = SekibanJsonHelper.Deserialize(dbCommand.Payload, typeof(JsonElement));
        if (payload is null)
        {
            return null;
        }
        var callHistories = SekibanJsonHelper.Deserialize<List<CallHistory>>(dbCommand.CallHistories) ??
            new List<CallHistory>();
        return new CommandDocumentForJsonExport
        {
            Id = dbCommand.Id,
            AggregateId = dbCommand.AggregateId,
            PartitionKey = dbCommand.PartitionKey,
            DocumentType = dbCommand.DocumentType,
            DocumentTypeName = dbCommand.DocumentTypeName,
            ExecutedUser = dbCommand.ExecutedUser,
            Exception = dbCommand.Exception,
            CallHistories = callHistories,
            Payload = payload,
            TimeStamp = dbCommand.TimeStamp,
            SortableUniqueId = dbCommand.SortableUniqueId,
            AggregateType = dbCommand.AggregateType,
            RootPartitionKey = dbCommand.RootPartitionKey
        };
    }


    private IEnumerable<IEnumerable<IEvent>> GetEventsInBatches(IEnumerable<IDbEvent> events)
    {
        const int batchSize = 1000;
        List<IEvent> eventBatch = [];

        foreach (var eventItem in events)
        {
            eventBatch.Add(FromDbEvent(eventItem)!);

            if (eventBatch.Count >= batchSize)
            {
                yield return eventBatch;
                eventBatch = [];
            }
        }

        if (eventBatch.Any())
        {
            yield return eventBatch;
        }
    }
    private IEvent? FromDbEvent(IDbEvent dbEvent)
    {
        if (string.IsNullOrEmpty(dbEvent.DocumentTypeName))
        {
            return null;
        }
        var type = registeredEventTypes.RegisteredTypes.FirstOrDefault(m => m.Name == dbEvent.DocumentTypeName);
        if (type is null)
        {
            return null;
        }
        if (SekibanJsonHelper.Deserialize(dbEvent.Payload, type) is not IEventPayloadCommon payload)
        {
            return null;
        }
        var callHistories = SekibanJsonHelper.Deserialize<List<CallHistory>>(dbEvent.CallHistories) ??
            new List<CallHistory>();
        return Event.GenerateIEvent(
            dbEvent.Id,
            dbEvent.AggregateId,
            dbEvent.PartitionKey,
            dbEvent.DocumentType,
            dbEvent.DocumentTypeName,
            type,
            dbEvent.TimeStamp,
            dbEvent.SortableUniqueId,
            payload,
            dbEvent.AggregateType,
            dbEvent.Version,
            dbEvent.RootPartitionKey,
            callHistories);
    }
}

using System.Text.Json;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.History;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentRepository(
    IndexedDbFactory dbFactory,
    RegisteredEventTypes registeredEventTypes,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor
) : IDocumentPersistentRepository, IEventPersistentRepository
{
    public async Task GetAllCommandStringsForAggregateIdAsync(Guid aggregateId, Type aggregatePayloadType, string? sinceSortableUniqueId, string rootPartitionKey, Action<IEnumerable<string>> resultAction) =>
        await dbFactory.DbActionAsync(
            async dbContext =>
            {
                var dbCommands = await dbContext.GetCommandsAsync(DbCommandQuery.ForAggregateId(aggregateId, aggregatePayloadType, sinceSortableUniqueId, rootPartitionKey));

                var commands = dbCommands
                    .Select(x => FromDbCommand(x))
                    .Where(x => x is not null)
                    .Select(x => JsonSerializer.Serialize(x));

                resultAction(commands);
            });

    public async Task<ResultBox<bool>> GetEvents(EventRetrievalInfo eventRetrievalInfo, Action<IEnumerable<IEvent>> resultAction)
    {
        var dbEvents = await dbFactory.DbActionAsync((dbContext) =>
            eventRetrievalInfo.GetAggregateContainerGroup() switch
            {
                AggregateContainerGroup.Default => dbContext.GetEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)),
                AggregateContainerGroup.Dissolvable => dbContext.GetDissolvableEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)),
                _ => throw new NotImplementedException(),
            });

        var events = dbEvents
            .Select(x => FromDbEvent(x, registeredEventTypes))
            .Where(x => x is not null)
            .Cast<IEvent>();

        resultAction(events);

        return true;
    }

    public async Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, int version, string rootPartitionKey, string payloadVersionIdentifier)
    {
        var query = new DbSingleProjectionSnapshotQuery(aggregateId, aggregatePayloadType, projectionPayloadType, version, rootPartitionKey, payloadVersionIdentifier, true);

        return (await dbFactory.DbActionAsync(
            async dbContext =>
                await dbContext.GetSingleProjectionSnapshotsAsync(query)
        )).Length != 0;
    }

    public async Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey, string payloadVersionIdentifier)
    {
        var query = new DbSingleProjectionSnapshotQuery(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey, payloadVersionIdentifier, true);

        var dbSnapshot = (
            await dbFactory.DbActionAsync(
                async dbContext =>
                    await dbContext.GetSingleProjectionSnapshotsAsync(query)
            )
        )
            .FirstOrDefault();

        if (dbSnapshot is null)
        {
            return null;
        }

        return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(FromDbSingleProjectionSnapshot(dbSnapshot)!);
    }

    public Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(Type multiProjectionPayloadType, string payloadVersionIdentifier, string rootPartitionKey = "")
    {
        throw new NotImplementedException();
    }

    public async Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey, string rootPartitionKey)
    {
        var query = new DbSingleProjectionSnapshotQuery(id, aggregateId, aggregatePayloadType, partitionKey, rootPartitionKey, true);

        var dbSnapshot = (
            await dbFactory.DbActionAsync(
                async dbContext =>
                    await dbContext.GetSingleProjectionSnapshotsAsync(query)
            )
        )
            .FirstOrDefault();

        if (dbSnapshot is null)
        {
            return null;
        }

        return await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(FromDbSingleProjectionSnapshot(dbSnapshot)!);
    }

    public async Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        var query = new DbSingleProjectionSnapshotQuery(aggregateId, aggregatePayloadType, projectionPayloadType, rootPartitionKey, false);

        var dbSnapshots = await dbFactory.DbActionAsync(
            async dbContext => await dbContext.GetSingleProjectionSnapshotsAsync(query));

        var snapshots = new List<SnapshotDocument>();

        foreach (var db in dbSnapshots)
        {
            var snapshot = FromDbSingleProjectionSnapshot(db);

            if (snapshot is null)
            {
                continue;
            }

            var filled = await singleProjectionSnapshotAccessor.FillSnapshotDocumentAsync(snapshot);

            if (filled is null)
            {
                continue;
            }

            snapshots.Add(snapshot);
        }

        return snapshots;
    }

    private static IEvent? FromDbEvent(DbEvent dbEvent, RegisteredEventTypes registeredEventTypes)
    {
        if (string.IsNullOrEmpty(dbEvent.DocumentTypeName))
        {
            return null;
        }

        var type = registeredEventTypes.RegisteredTypes.FirstOrDefault(x => x.Name == dbEvent.DocumentTypeName);
        if (type is null)
        {
            return null;
        }

        if (SekibanJsonHelper.Deserialize(dbEvent.Payload, type) is not IEventPayloadCommon payload)
        {
            return null;
        }

        var callHistories = SekibanJsonHelper.Deserialize<List<CallHistory>>(dbEvent.CallHistories) ?? [];

        return Event.GenerateIEvent(
            new Guid(dbEvent.Id),
            new Guid(dbEvent.AggregateId),
            dbEvent.PartitionKey,
            Enum.Parse<DocumentType>(dbEvent.DocumentType),
            dbEvent.DocumentTypeName,
            type,
            DateTimeConverter.ToDateTime(dbEvent.TimeStamp),
            dbEvent.SortableUniqueId,
            payload,
            dbEvent.AggregateType,
            dbEvent.Version,
            dbEvent.RootPartitionKey,
            callHistories
        );
    }

    private static CommandDocumentForJsonExport? FromDbCommand(DbCommand dbCommand)
    {
        var payload = SekibanJsonHelper.Deserialize<JsonElement?>(dbCommand.Payload);
        if (payload is null)
        {
            return null;
        }

        var callHistories = SekibanJsonHelper.Deserialize<List<CallHistory>>(dbCommand.CallHistories) ?? [];

        return new CommandDocumentForJsonExport
        {
            Id = new Guid(dbCommand.Id),
            AggregateId = new Guid(dbCommand.AggregateId),
            PartitionKey = dbCommand.PartitionKey,
            DocumentType = Enum.Parse<DocumentType>(dbCommand.DocumentType),
            DocumentTypeName = dbCommand.DocumentTypeName,
            ExecutedUser = dbCommand.ExecutedUser,
            Exception = dbCommand.Exception,
            CallHistories = callHistories,
            Payload = payload,
            TimeStamp = DateTimeConverter.ToDateTime(dbCommand.TimeStamp),
            SortableUniqueId = dbCommand.SortableUniqueId,
            AggregateType = dbCommand.AggregateType,
            RootPartitionKey = dbCommand.RootPartitionKey
        };
    }

    private static SnapshotDocument? FromDbSingleProjectionSnapshot(DbSingleProjectionSnapshot dbSnapshot)
    {
        var payload = dbSnapshot.Snapshot is null ? null : SekibanJsonHelper.Deserialize<JsonElement?>(dbSnapshot.Snapshot);

        return new()
        {
            Id = new Guid(dbSnapshot.Id),
            AggregateId = new Guid(dbSnapshot.AggregateId),
            PartitionKey = dbSnapshot.PartitionKey,
            DocumentType = Enum.Parse<DocumentType>(dbSnapshot.DocumentType),
            DocumentTypeName = dbSnapshot.DocumentTypeName,
            TimeStamp = DateTimeConverter.ToDateTime(dbSnapshot.TimeStamp),
            SortableUniqueId = dbSnapshot.SortableUniqueId,
            AggregateType = dbSnapshot.AggregateType,
            RootPartitionKey = dbSnapshot.RootPartitionKey,
            Snapshot = payload,
            LastEventId = new Guid(dbSnapshot.LastEventId),
            LastSortableUniqueId = dbSnapshot.LastSortableUniqueId,
            SavedVersion = dbSnapshot.SavedVersion,
            PayloadVersionIdentifier = dbSnapshot.PayloadVersionIdentifier
        };
    }
}

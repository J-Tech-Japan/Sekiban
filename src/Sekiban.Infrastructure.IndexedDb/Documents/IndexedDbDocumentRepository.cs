using System.Text.Json;
using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.History;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Infrastructure.IndexedDb.Databases;

namespace Sekiban.Infrastructure.IndexedDb.Documents;

public class IndexedDbDocumentRepository(
    IndexedDbFactory dbFactory,
    RegisteredEventTypes registeredEventTypes
) : IDocumentPersistentRepository, IEventPersistentRepository
{
    public Task<bool> ExistsSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, int version, string rootPartitionKey, string payloadVersionIdentifier)
    {
        throw new NotImplementedException();
    }

    public async Task GetAllCommandStringsForAggregateIdAsync(Guid aggregateId, Type aggregatePayloadType, string? sinceSortableUniqueId, string rootPartitionKey, Action<IEnumerable<string>> resultAction)
    {
        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregatePayloadType);

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
    }

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
            .Select(x => FromDbEvent(x))
            .Where(x => x is not null)
            .Cast<IEvent>();

        resultAction(events);

        return true;
    }

    public Task<SnapshotDocument?> GetLatestSnapshotForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey, string payloadVersionIdentifier)
    {
        throw new NotImplementedException();
    }

    public Task<MultiProjectionSnapshotDocument?> GetLatestSnapshotForMultiProjectionAsync(Type multiProjectionPayloadType, string payloadVersionIdentifier, string rootPartitionKey = "")
    {
        throw new NotImplementedException();
    }

    public Task<SnapshotDocument?> GetSnapshotByIdAsync(Guid id, Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string partitionKey, string rootPartitionKey)
    {
        throw new NotImplementedException();
    }

    public Task<List<SnapshotDocument>> GetSnapshotsForAggregateAsync(Guid aggregateId, Type aggregatePayloadType, Type projectionPayloadType, string rootPartitionKey = "default")
    {
        throw new NotImplementedException();
    }

    private IEvent? FromDbEvent(DbEvent dbEvent)
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

    private CommandDocumentForJsonExport? FromDbCommand(DbCommand dbCommand)
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
}

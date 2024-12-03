using ResultBoxes;
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

    public Task GetAllCommandStringsForAggregateIdAsync(Guid aggregateId, Type aggregatePayloadType, string? sinceSortableUniqueId, string rootPartitionKey, Action<IEnumerable<string>> resultAction)
    {
        throw new NotImplementedException();
    }

    public async Task<ResultBox<bool>> GetEvents(EventRetrievalInfo eventRetrievalInfo, Action<IEnumerable<IEvent>> resultAction)
    {
        var dbEvents = await dbFactory.DbActionAsync((dbContext) =>
            dbContext.GetEventsAsync(DbEventQuery.FromEventRetrievalInfo(eventRetrievalInfo)));

        var events = dbEvents
            .Select(x => FromDbEvent(x));

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
}

using Microsoft.EntityFrameworkCore;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Serialize;
using System.Text.Json.Nodes;
namespace Sekiban.Pure.Postgres;

public class PostgresDbEventReader : IEventReader
{
    private readonly PostgresDbFactory _dbFactory;
    private readonly IEventTypes _eventTypes;
    private readonly ISekibanSerializer _serializer;

    public PostgresDbEventReader(PostgresDbFactory dbFactory, SekibanDomainTypes sekibanDomainTypes)
    {
        _dbFactory = dbFactory;
        _eventTypes = sekibanDomainTypes.EventTypes;
        _serializer = sekibanDomainTypes.Serializer;
    }

    public async Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo eventRetrievalInfo)
    {
        return await _dbFactory.DbActionAsync(
            async dbContext =>
            {
                var query = dbContext.Events.AsQueryable();

                // Handle partition-based query
                if (eventRetrievalInfo.GetIsPartition())
                {
                    var partitionKey = PartitionKeys
                        .Existing(
                            eventRetrievalInfo.AggregateId.GetValue(),
                            eventRetrievalInfo.AggregateStream.GetValue().GetSingleStreamName().UnwrapBox(),
                            eventRetrievalInfo.RootPartitionKey.GetValue())
                        .ToPrimaryKeysString();
                    query = query.Where(e => e.PartitionKey == partitionKey);
                } else
                {
                    // Handle non-partition query
                    if (eventRetrievalInfo.HasAggregateStream())
                    {
                        var aggregates = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
                        query = query.Where(e => aggregates.Contains(e.AggregateGroup));
                    }

                    if (eventRetrievalInfo.HasRootPartitionKey())
                    {
                        query = query.Where(e => e.RootPartitionKey == eventRetrievalInfo.RootPartitionKey.GetValue());
                    }
                }

                // Apply SortableId conditions
                query = eventRetrievalInfo.SortableIdCondition switch
                {
                    SinceSortableIdCondition since => query
                        .Where(e => string.Compare(e.SortableUniqueId, since.SortableUniqueId.Value) > 0)
                        .OrderBy(e => e.SortableUniqueId),
                    BetweenSortableIdCondition between => query
                        .Where(
                            e => string.Compare(e.SortableUniqueId, between.Start.Value) > 0 &&
                                string.Compare(e.SortableUniqueId, between.End.Value) < 0)
                        .OrderBy(e => e.SortableUniqueId),
                    SortableIdConditionNone => query.OrderBy(e => e.SortableUniqueId),
                    _ => throw new ArgumentOutOfRangeException(nameof(eventRetrievalInfo.SortableIdCondition))
                };

                // Apply MaxCount if specified
                if (eventRetrievalInfo.MaxCount.HasValue)
                {
                    query = query.Take(eventRetrievalInfo.MaxCount.GetValue());
                }

                var dbEvents = await query.ToListAsync();
                var events = new List<IEvent>();

                foreach (var dbEvent in dbEvents)
                {
                    if (string.IsNullOrWhiteSpace(dbEvent.PayloadTypeName)) continue;

                    var jsonPayload = _serializer.Deserialize<JsonNode>(
                        dbEvent.Payload);


                    if (jsonPayload is null) continue;

                    var eventDocument = new EventDocumentCommon(
                        dbEvent.Id,
                        jsonPayload,
                        dbEvent.SortableUniqueId,
                        dbEvent.Version,
                        dbEvent.AggregateId,
                        dbEvent.AggregateGroup,
                        dbEvent.RootPartitionKey,
                        dbEvent.PayloadTypeName,
                        dbEvent.TimeStamp,
                        dbEvent.PartitionKey,
                        new EventMetadata(dbEvent.CausationId, dbEvent.CorrelationId, dbEvent.ExecutedUser)
                    );

                    var converted = _eventTypes.DeserializeToTyped(
                        eventDocument,
                        _serializer.GetJsonSerializerOptions());
                    if (converted.IsSuccess)
                    {
                        events.Add(converted.GetValue());
                    }
                }

                return events;
            });
    }
}
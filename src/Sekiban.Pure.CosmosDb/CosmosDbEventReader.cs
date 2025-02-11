using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.OrleansEventSourcing;

namespace Sekiban.Pure.CosmosDb;

public class CosmosDbEventReader(CosmosDbFactory dbFactory, IEventTypes eventTypes) : IEventReader
{
    private const int DefaultOptionsMax = -1;

    public async Task<ResultBox<IReadOnlyList<IEvent>>> GetEvents(EventRetrievalInfo eventRetrievalInfo)
    {
        return await dbFactory.CosmosActionAsync(
            DocumentType.Event,
            async container =>
            {
                if (eventRetrievalInfo.GetIsPartition())
                {
                    var options = CreateDefaultOptions();
                    options.PartitionKey = CosmosPartitionGenerator.ForAggregate(PartitionKeys.Existing(
                        eventRetrievalInfo.AggregateId.GetValue(),
                        eventRetrievalInfo.AggregateStream.GetValue().GetSingleStreamName().UnwrapBox(),
                        eventRetrievalInfo.RootPartitionKey.GetValue()));
                    var query = container.GetItemLinqQueryable<EventDocumentCommon>(
                        linqSerializerOptions: new CosmosLinqSerializerOptions
                            { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase });
                    query = eventRetrievalInfo.SortableIdCondition switch
                    {
                        (SinceSortableIdCondition since) => query
                            .Where(m => m.SortableUniqueId.CompareTo(since.SortableUniqueId.Value) > 0)
                            .OrderBy(m => m.SortableUniqueId),
                        (BetweenSortableIdCondition between) => query
                            .Where(m => m.SortableUniqueId.CompareTo(between.Start.Value) > 0 &&
                                        m.SortableUniqueId.CompareTo(between.End.Value) < 0)
                            .OrderBy(m => m.SortableUniqueId),
                        SortableIdConditionNone => query.OrderBy(m => m.SortableUniqueId),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    var feedIterator = container.GetItemQueryIterator<EventDocumentCommon>(
                        query.ToQueryDefinition(),
                        null,
                        options);
                    var events = new List<IEvent>();
                    while (feedIterator.HasMoreResults)
                    {
                        var response = await feedIterator.ReadNextAsync();
                        var toAdds = ProcessEvents(response, eventRetrievalInfo.SortableIdCondition);
                        events.AddRange(toAdds);
                        if (eventRetrievalInfo.MaxCount.HasValue &&
                            events.Count > eventRetrievalInfo.MaxCount.GetValue())
                        {
                            events = events.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList();
                            break;
                        }
                    }

                    return events;
                }
                else
                {
                    var options = CreateDefaultOptions();

                    var query = container
                        .GetItemLinqQueryable<EventDocumentCommon>(
                            linqSerializerOptions: new CosmosLinqSerializerOptions
                                { PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase }).AsQueryable();
                    if (eventRetrievalInfo.HasAggregateStream())
                    {
                        var aggregates = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames();
                        query = query.Where(m => aggregates.Contains(m.AggregateGroup));
                    }

                    if (eventRetrievalInfo.HasRootPartitionKey())
                        query = query.Where(m => m.RootPartitionKey == eventRetrievalInfo.RootPartitionKey.GetValue());
                    query = eventRetrievalInfo.SortableIdCondition switch
                    {
                        (SinceSortableIdCondition since) => query
                            .Where(m => m.SortableUniqueId.CompareTo(since.SortableUniqueId.Value) > 0)
                            .OrderBy(m => m.SortableUniqueId),
                        BetweenSortableIdCondition between => query
                            .Where(m => m.SortableUniqueId.CompareTo(between.Start.Value) > 0 &&
                                        m.SortableUniqueId.CompareTo(between.End.Value) < 0)
                            .OrderBy(m => m.SortableUniqueId),
                        (SortableIdConditionNone _) => query.OrderBy(m => m.SortableUniqueId),
                        _ => throw new ArgumentOutOfRangeException()
                    };
                    var feedIterator = container.GetItemQueryIterator<EventDocumentCommon>(
                        query.ToQueryDefinition(),
                        null,
                        options);
                    var events = new List<IEvent>();
                    while (feedIterator.HasMoreResults)
                    {
                        var response = await feedIterator.ReadNextAsync();
                        var toAdds = ProcessEvents(response, eventRetrievalInfo.SortableIdCondition);
                        events.AddRange(toAdds);
                        if (eventRetrievalInfo.MaxCount.HasValue &&
                            events.Count > eventRetrievalInfo.MaxCount.GetValue())
                        {
                            events = events.Take(eventRetrievalInfo.MaxCount.GetValue()).ToList();
                            break;
                        }
                    }

                    return events;
                }
            });
    }

    private static QueryRequestOptions CreateDefaultOptions()
    {
        return new QueryRequestOptions
        {
            MaxConcurrency = DefaultOptionsMax, MaxItemCount = DefaultOptionsMax,
            MaxBufferedItemCount = DefaultOptionsMax
        };
    }

    private List<IEvent> ProcessEvents(IEnumerable<EventDocumentCommon> response,
        ISortableIdCondition sortableIdCondition)
    {
        var events = new List<IEvent>();
        foreach (var item in response)
        {
            // pick out one item
            if (string.IsNullOrWhiteSpace(item.PayloadTypeName)) continue;

            var converted = eventTypes.DeserializeToTyped(item, dbFactory.GetJsonSerializerOptions());
            if (converted.IsSuccess) events.Add(converted.GetValue());
            // var toAdd = (registeredEventTypes
            //                  .RegisteredTypes
            //                  .Where(m => m.Name == typeName)
            //                  .Select(m => SekibanJsonHelper.ConvertTo(item, typeof(Event<>).MakeGenericType(m)) as IEvent)
            //                  .FirstOrDefault(m => m is not null) ??
            //              EventHelper.GetUnregisteredEvent(item)) ??
            //             throw new SekibanUnregisteredEventFoundException();
            // if (sortableIdCondition.OutsideOfRange(toAdd.GetSortableUniqueId()))
            // {
            //     continue;
            // }
            //
            // events.Add(toAdd);
        }

        return events;
    }
}
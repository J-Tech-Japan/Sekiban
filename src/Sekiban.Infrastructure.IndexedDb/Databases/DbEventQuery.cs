using ResultBoxes;
using Sekiban.Core.Documents.Pools;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbEventQuery
{
    public string? RootPartitionKey { get; init; }
    public string? PartitionKey { get; init; }
    public string[]? AggregateTypes { get; init; }
    public string? SortableIdStart { get; init; }
    public string? SortableIdEnd { get; init; }

    public int? MaxCount { get; init; }

    public static DbEventQuery FromEventRetrievalInfo(EventRetrievalInfo eventRetrievalInfo)
    {
        var query = eventRetrievalInfo.GetIsPartition() ?
            new DbEventQuery
            {
                PartitionKey = eventRetrievalInfo.GetPartitionKey().UnwrapBox(),
            } :
            new DbEventQuery
            {
                RootPartitionKey = eventRetrievalInfo.HasRootPartitionKey() ? eventRetrievalInfo.RootPartitionKey.GetValue() : null,
                AggregateTypes = eventRetrievalInfo.HasAggregateStream() ? eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames().ToArray() : null,
            };

        query = query with
        {
            MaxCount = eventRetrievalInfo.MaxCount.HasValue ? eventRetrievalInfo.MaxCount.GetValue() : null,
        };

        switch (eventRetrievalInfo.SortableIdCondition)
        {
            case SortableIdConditionNone:
                break;

            case SinceSortableIdCondition since:
                query = query with
                {
                    SortableIdStart = since.SortableUniqueId,
                };
                break;

            case BetweenSortableIdCondition between:
                query = query with
                {
                    SortableIdStart = between.Start,
                    SortableIdEnd = between.End,
                };
                break;


            default:
                throw new NotImplementedException("unknown ISortableIdCondition");
        }

        return query;
    }
}

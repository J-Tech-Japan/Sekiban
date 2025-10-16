using ResultBoxes;
using Sekiban.Core.Documents.Pools;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbEventQuery
{
    public string? RootPartitionKey { get; private set; }
    public string? PartitionKey { get; private set; }
    public string[]? AggregateTypes { get; private set; }
    public string? SortableIdStart { get; private set; }
    public string? SortableIdEnd { get; private set; }

    public int? MaxCount { get; private set; }

    public DbEventQuery WithSortableIdStart(string? sortableIdStart) =>
        this with { SortableIdStart = sortableIdStart };

    public DbEventQuery WithMaxCount(int? maxCount) =>
        this with { MaxCount = maxCount };

    public static DbEventQuery FromEventRetrievalInfo(EventRetrievalInfo eventRetrievalInfo)
    {
        var query = new DbEventQuery();

        if (eventRetrievalInfo.GetIsPartition())
        {
            query.PartitionKey = eventRetrievalInfo.GetPartitionKey().UnwrapBox();
        }
        else
        {
            if (eventRetrievalInfo.HasRootPartitionKey())
            {
                query.RootPartitionKey = eventRetrievalInfo.RootPartitionKey.GetValue();
            }

            if (eventRetrievalInfo.HasAggregateStream())
            {
                query.AggregateTypes = eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames().ToArray();
            }
        }

        if (eventRetrievalInfo.MaxCount.HasValue)
        {
            query.MaxCount = eventRetrievalInfo.MaxCount.GetValue();
        }

        switch (eventRetrievalInfo.SortableIdCondition)
        {
            case SortableIdConditionNone:
                break;

            case SinceSortableIdCondition since:
                query.SortableIdStart = since.SortableUniqueId;
                break;

            case BetweenSortableIdCondition between:
                query.SortableIdStart = between.Start;
                query.SortableIdEnd = between.End;
                break;

            default:
                throw new NotImplementedException("unknown ISortableIdCondition");
        }

        return query;
    }
}

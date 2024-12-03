using Sekiban.Core.Documents.Pools;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbEventQuery
{
    public string? RootPartitionKey { get; init; }
    public string? PartitionKey { get; init; }
    public string[]? AggregateTypes { get; init; }
    public string? SortableIdStart { get; init; }
    public string? SortableIdEnd { get; init; }

    public static DbEventQuery FromEventRetrievalInfo(EventRetrievalInfo eventRetrievalInfo)
    {
        var query = new DbEventQuery
        {
            RootPartitionKey = eventRetrievalInfo.HasRootPartitionKey() ? eventRetrievalInfo.RootPartitionKey.GetValue() : null,
            PartitionKey = eventRetrievalInfo.GetIsPartition() ? eventRetrievalInfo.GetPartitionKey().GetValue() : null,
            AggregateTypes = eventRetrievalInfo.HasAggregateStream() ? eventRetrievalInfo.AggregateStream.GetValue().GetStreamNames().ToArray() : null,
        };

        switch (eventRetrievalInfo.SortableIdCondition)
        {
            case null:
                break;

            case SinceSortableIdCondition since: {
                query = query with {
                    SortableIdStart = since.SortableUniqueId,
                };
                break;
            }

            case BetweenSortableIdCondition between: {
                query = query with {
                    SortableIdStart = between.Start,
                    SortableIdEnd = between.End,
                };
                break;
            }

            default:
                throw new NotImplementedException("unknown ISortableIdCondition");
        }

        return query;
    }
}

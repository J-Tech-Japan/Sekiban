using Sekiban.Pure.Documents;
namespace Sekiban.Pure.OrleansEventSourcing;

public record BetweenSortableIdCondition(SortableUniqueIdValue Start, SortableUniqueIdValue End) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) =>
        Start.IsLaterThan(toCompare) || End.IsEarlierThan(toCompare);
}
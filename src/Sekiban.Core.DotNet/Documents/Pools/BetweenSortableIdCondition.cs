using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record BetweenSortableIdCondition(SortableUniqueIdValue Start, SortableUniqueIdValue End) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) =>
        Start.IsLaterThanOrEqual(toCompare) || End.IsEarlierThanOrEqual(toCompare);
}

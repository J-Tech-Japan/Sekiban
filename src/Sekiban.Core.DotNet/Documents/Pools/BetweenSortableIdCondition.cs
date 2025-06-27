using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record BetweenSortableIdCondition(SortableUniqueIdValue Start, SortableUniqueIdValue End) : ISortableIdCondition
{
    public bool InsideOfRange(SortableUniqueIdValue toCompare) =>
        toCompare.IsLaterThan(Start) && toCompare.IsEarlierThan(End);

    public bool OutsideOfRange(SortableUniqueIdValue toCompare) =>
        !InsideOfRange(toCompare);
}

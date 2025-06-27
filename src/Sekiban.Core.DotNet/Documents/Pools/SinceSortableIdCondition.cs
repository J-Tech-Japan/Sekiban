using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record SinceSortableIdCondition(SortableUniqueIdValue SortableUniqueId) : ISortableIdCondition
{
    public bool InsideOfRange(SortableUniqueIdValue toCompare) =>
        toCompare.IsLaterThan(SortableUniqueId);

    public bool OutsideOfRange(SortableUniqueIdValue toCompare) =>
        !InsideOfRange(toCompare);
}

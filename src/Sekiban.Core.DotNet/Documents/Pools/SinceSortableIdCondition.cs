using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record SinceSortableIdCondition(SortableUniqueIdValue SortableUniqueId) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => SortableUniqueId.IsLaterThanOrEqual(toCompare);
}

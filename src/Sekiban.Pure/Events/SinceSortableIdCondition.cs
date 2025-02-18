using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Events;

public record SinceSortableIdCondition(SortableUniqueIdValue SortableUniqueId) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => SortableUniqueId.IsLaterThan(toCompare);
}
using Sekiban.Pure.Documents;
namespace Sekiban.Pure.OrleansEventSourcing;

public record SinceSortableIdCondition(SortableUniqueIdValue SortableUniqueId) : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => SortableUniqueId.IsLaterThan(toCompare);
}
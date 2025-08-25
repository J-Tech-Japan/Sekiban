using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Events;

public record SortableIdConditionNone : ISortableIdCondition
{
    public static SortableIdConditionNone None => new();
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => false;
}

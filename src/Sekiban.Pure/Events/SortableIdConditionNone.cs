using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Events;

public record SortableIdConditionNone : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => false;
    public static SortableIdConditionNone None => new();
}
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record SortableIdConditionNone : ISortableIdCondition
{
    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => false;
}

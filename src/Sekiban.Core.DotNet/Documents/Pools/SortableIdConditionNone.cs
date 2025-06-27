using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents.Pools;

public record SortableIdConditionNone : ISortableIdCondition
{
    public bool InsideOfRange(SortableUniqueIdValue toCompare) => true;

    public bool OutsideOfRange(SortableUniqueIdValue toCompare) => false;
}

using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Documents;
namespace Sekiban.Pure.OrleansEventSourcing;

public interface ISortableIdCondition
{
    public static ISortableIdCondition None => new SortableIdConditionNone();
    public bool OutsideOfRange(SortableUniqueIdValue toCompare);
    public static ISortableIdCondition Since(SortableUniqueIdValue sinceSortableId) =>
        new SinceSortableIdCondition(sinceSortableId);
    public static ISortableIdCondition Between(SortableUniqueIdValue start, SortableUniqueIdValue end) =>
        start.IsEarlierThan(end)
            ? new BetweenSortableIdCondition(start, end)
            : new BetweenSortableIdCondition(end, start);
    public static ISortableIdCondition FromState(IAggregate? state) =>
        state?.LastSortableUniqueId is { } lastSortableId ? Since(lastSortableId) : None;
}
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Query;
using Sekiban.Core.Query.SingleProjections.Projections;
namespace Sekiban.Core.Documents.Pools;

public interface ISortableIdCondition
{
    public static ISortableIdCondition None => new SortableIdConditionNone();
    public bool OutsideOfRange(SortableUniqueIdValue toCompare);
    public static ISortableIdCondition Since(SortableUniqueIdValue sinceSortableId) =>
        new SinceSortableIdCondition(sinceSortableId);
    public static ISortableIdCondition FromState(IAggregateStateCommon? state) =>
        state?.LastSortableUniqueId is { } lastSortableId ? Since(lastSortableId) : None;
    public static ISortableIdCondition FromMultiProjectionState(IProjection state) =>
        state.LastSortableUniqueId is { } lastSortableId && state.Version > 0 ? Since(lastSortableId) : None;
    public static ISortableIdCondition FromMemoryCacheContainer(IMemoryCacheContainer container) =>
        container.SafeSortableUniqueId is { } lastSortableId ? Since(lastSortableId) : None;
}

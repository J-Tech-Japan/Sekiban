using Sekiban.Core.Document.ValueObjects;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjection
{
    Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        int? toVersion = null,
        SortableUniqueIdValue? includesSortableUniqueId = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection,
        ISingleProjectionStateConvertible<TState>
        where TState : IAggregateCommon
        where TProjector : ISingleProjector<TProjection>, new();
}

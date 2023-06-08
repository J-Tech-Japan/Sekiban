using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public interface ISingleProjection
{
    Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SortableUniqueIdValue? includesSortableUniqueId = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new();
}

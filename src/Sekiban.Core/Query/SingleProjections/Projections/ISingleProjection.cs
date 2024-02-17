using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     General single projection interface.
///     Developers does not need to implement this interface directly.
/// </summary>
public interface ISingleProjection
{
    Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new();
}

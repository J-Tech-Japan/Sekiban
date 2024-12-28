using Sekiban.Core.Aggregate;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Default Single Projector implementation.
///     Default means Aggregate itself as a projection.
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
public class DefaultSingleProjector<TAggregatePayload> : ISingleProjector<Aggregate<TAggregatePayload>>
    where TAggregatePayload : IAggregatePayloadCommon
{
    public Aggregate<TAggregatePayload> CreateInitialAggregate(Guid aggregateId) =>
        AggregateCommon.Create<Aggregate<TAggregatePayload>>(aggregateId);
    public Aggregate<TAggregatePayload> CreateAggregateFromState(
        Aggregate<TAggregatePayload> current,
        object state,
        SekibanAggregateTypes sekibanAggregateTypes) =>
        throw new NotImplementedException();

    public Type GetOriginalAggregatePayloadType() =>
        typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
    public Type GetPayloadType() => typeof(TAggregatePayload).GetBaseAggregatePayloadTypeFromAggregate();
}

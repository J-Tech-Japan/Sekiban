namespace Sekiban.Core.Aggregate;

public interface IAggregateSubtypePayload<TParentAggregatePayload> : IAggregatePayloadCommon, IApplicableAggregatePayload<TParentAggregatePayload>
    where TParentAggregatePayload : IParentAggregatePayloadCommon<TParentAggregatePayload>
{
}

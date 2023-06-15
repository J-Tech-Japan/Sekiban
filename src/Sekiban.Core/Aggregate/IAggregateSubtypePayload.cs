namespace Sekiban.Core.Aggregate;

public interface IAggregateSubtypePayload<TParentAggregatePayload> : IApplicableAggregatePayload<TParentAggregatePayload>
    where TParentAggregatePayload : IParentAggregatePayloadCommon<TParentAggregatePayload>
{
}

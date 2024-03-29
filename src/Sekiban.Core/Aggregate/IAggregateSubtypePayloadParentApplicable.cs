namespace Sekiban.Core.Aggregate;

public interface IAggregateSubtypePayloadParentApplicable<TParentAggregatePayload> : IAggregateSubtypePayloadCommon,
    IApplicableAggregatePayload<TParentAggregatePayload> where TParentAggregatePayload : IAggregatePayloadCommon;

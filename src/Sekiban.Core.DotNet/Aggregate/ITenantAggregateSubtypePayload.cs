namespace Sekiban.Core.Aggregate;

public interface
    ITenantAggregateSubtypePayload<TParentAggregatePayload, TSubtypeAggregatePayload> :
    IAggregateSubtypePayloadParentApplicable<TParentAggregatePayload>,
    IAggregatePayloadGeneratable<TSubtypeAggregatePayload>,
    ITenantAggregatePayloadCommon<TSubtypeAggregatePayload> where TParentAggregatePayload : IAggregatePayloadCommon
    where TSubtypeAggregatePayload : ITenantAggregateSubtypePayload<TParentAggregatePayload, TSubtypeAggregatePayload>;

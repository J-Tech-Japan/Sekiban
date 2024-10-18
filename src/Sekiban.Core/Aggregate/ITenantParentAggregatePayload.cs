namespace Sekiban.Core.Aggregate;

public interface
    ITenantParentAggregatePayload<TParentAggregatePayload> :
    IParentAggregatePayload<TParentAggregatePayload, TParentAggregatePayload>,
    IAggregatePayloadGeneratable<TParentAggregatePayload>,
    ITenantAggregatePayloadCommon<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadGeneratable<TParentAggregatePayload>,
    ITenantParentAggregatePayload<TParentAggregatePayload>;

namespace Sekiban.Core.Aggregate;

public interface ITenantAggregatePayload<TAggregatePayload> : IAggregatePayloadGeneratable<TAggregatePayload>,
    ITenantAggregatePayloadCommon<TAggregatePayload>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>, IEquatable<TAggregatePayload>,
    ITenantAggregatePayload<TAggregatePayload>;

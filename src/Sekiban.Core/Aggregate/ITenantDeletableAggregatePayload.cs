namespace Sekiban.Core.Aggregate;

public interface
    ITenantDeletableAggregatePayload<TAggregatePayload> : ITenantAggregatePayload<TAggregatePayload>, IDeletable
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>, IEquatable<TAggregatePayload>,
    ITenantDeletableAggregatePayload<TAggregatePayload>;

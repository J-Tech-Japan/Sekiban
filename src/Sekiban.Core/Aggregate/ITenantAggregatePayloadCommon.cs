namespace Sekiban.Core.Aggregate;

public interface ITenantAggregatePayloadCommon<TAggregatePayload>
    where TAggregatePayload : ITenantAggregatePayloadCommon<TAggregatePayload>;

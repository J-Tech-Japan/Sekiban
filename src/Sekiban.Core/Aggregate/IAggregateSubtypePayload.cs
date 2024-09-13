namespace Sekiban.Core.Aggregate;

/// <summary>
///     Aggregate Payload for Subtype
///     You can implement this for your aggregate subtype payload class.
///     It will always use with <see cref="IParentAggregatePayload{TSelf,TFirst}" />
///     or <see cref="IParentAggregatePayload{TFirst}" />
/// </summary>
/// <typeparam name="TParentAggregatePayload">Parent Aggregate Payload</typeparam>
/// <typeparam name="TSubtypeAggregatePayload">Subtype Aggregate Payload (This record itself)</typeparam>
public interface
    IAggregateSubtypePayload<TParentAggregatePayload, TSubtypeAggregatePayload> :
    IAggregateSubtypePayloadParentApplicable<TParentAggregatePayload>,
    IAggregatePayloadGeneratable<TSubtypeAggregatePayload>,
    IAggregatePayloadCommon<TSubtypeAggregatePayload> where TParentAggregatePayload : IAggregatePayloadCommon
    where TSubtypeAggregatePayload : IAggregateSubtypePayload<TParentAggregatePayload, TSubtypeAggregatePayload>;
public interface
    ITenantAggregateSubtypePayload<TParentAggregatePayload, TSubtypeAggregatePayload> :
    IAggregateSubtypePayloadParentApplicable<TParentAggregatePayload>,
    IAggregatePayloadGeneratable<TSubtypeAggregatePayload>,
    ITenantAggregatePayloadCommon<TSubtypeAggregatePayload> where TParentAggregatePayload : IAggregatePayloadCommon
    where TSubtypeAggregatePayload : ITenantAggregateSubtypePayload<TParentAggregatePayload, TSubtypeAggregatePayload>;

namespace Sekiban.Core.Aggregate;

/// <summary>
///     Aggregate Payload for Subtype
///     You can implement this for your aggregate subtype payload class.
///     It will always use with <see cref="IParentAggregatePayload{TSelf,TFirst}" />
///     or <see cref="IParentAggregatePayload{TFirst}" />
/// </summary>
/// <typeparam name="TParentAggregatePayload">Parent Aggregate</typeparam>
public interface IAggregateSubtypePayload<TParentAggregatePayload> : IApplicableAggregatePayload<TParentAggregatePayload>
    where TParentAggregatePayload : IParentAggregatePayloadCommon<TParentAggregatePayload>
{
}

namespace Sekiban.Core.Aggregate;

// ReSharper disable once UnusedTypeParameter
/// <summary>
///     Means to be applicable to parent aggregate payload
///     Note : Developer does not need to implement this interface.
///     Automatically implemented by
///     <see cref="IAggregateSubtypePayload{TParentAggregatePayload, TSubtypeAggregatePayload}" />
/// </summary>
/// <typeparam name="TParentAggregatePayload"></typeparam>
public interface IApplicableAggregatePayload<TParentAggregatePayload> : IAggregatePayloadCommon
    where TParentAggregatePayload : IAggregatePayloadCommon;

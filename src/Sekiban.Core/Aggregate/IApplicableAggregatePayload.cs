namespace Sekiban.Core.Aggregate;

// ReSharper disable once UnusedTypeParameter
/// <summary>
///     Means to be applicable to parent aggregate payload
///     Note : Developer does not need to implement this interface.
///     Automatically implemented by <see cref="IAggregateSubtypePayload{TParentAggregatePayload}" />
/// </summary>
/// <typeparam name="TParentAggregatePayload"></typeparam>
public interface IApplicableAggregatePayload<TParentAggregatePayload> : IAggregatePayloadCommonBase
    where TParentAggregatePayload : IAggregatePayloadCommonBase;

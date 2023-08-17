namespace Sekiban.Core.Aggregate;

/// <summary>
///     Common interface for Parent Aggregate Payload
///     Note : No need to implement this interface. It is used internally.
/// </summary>
/// <typeparam name="TParentAggregatePayload">Refers Parent Aggregate Itself</typeparam>
public interface IParentAggregatePayloadCommon<TParentAggregatePayload> : IApplicableAggregatePayload<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommonBase;

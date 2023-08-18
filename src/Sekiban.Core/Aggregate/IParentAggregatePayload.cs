namespace Sekiban.Core.Aggregate;

/// <summary>
///     Parent Aggregate Payload
///     Developer can implement this interface to define the state of your aggregate.
///     This interface is used when Parent Aggregate Type and First Aggregate Payload type are same.
/// </summary>
/// <typeparam name="TParentAggregatePayload">
///     Parent Aggregate Payload Type. Usually it can be declare by repeating class itself.
///     In this declaration, Parent Aggregate Payload Type and First Aggregate Payload Type are same.
///     Example:
///     <code><![CDATA[
///      CartAggregate : IParentAggregatePayload<CartAggregate>
///      ]]></code>
/// </typeparam>
public interface IParentAggregatePayload<TParentAggregatePayload> : IParentAggregatePayload<TParentAggregatePayload, TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon, new();
/// <summary>
///     Parent Aggregate Payload
///     Developer can implement this interface to define the state of your aggregate.
///     This interface is used when Parent Aggregate Type and First Aggregate Payload type are different.
/// </summary>
/// <typeparam name="TParentAggregatePayload">
///     Parent Aggregate Payload Type. Usually it can be declare by repeating class itself.
///     Example:
///     <code><![CDATA[
///     CartAggregate : IParentAggregatePayload<CartAggregate, FirstAggregate>
///     ]]></code>
/// </typeparam>
/// <typeparam name="TFirstAggregatePayload">
///     First Aggregate essentially denotes the state when you initially establish the
///     aggregate. In other words, it refers to types when they initiate or start for the first time.
/// </typeparam>
// ReSharper disable once UnusedTypeParameter
public interface IParentAggregatePayload<TParentAggregatePayload, TFirstAggregatePayload> : IParentAggregatePayloadCommon<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon where TFirstAggregatePayload : IAggregatePayloadCommon;

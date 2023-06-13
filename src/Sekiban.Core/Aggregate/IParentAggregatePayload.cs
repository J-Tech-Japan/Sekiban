namespace Sekiban.Core.Aggregate;

public interface IParentAggregatePayload<TParentAggregatePayload> : IParentAggregatePayload<TParentAggregatePayload, TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon, new()
{
}
public interface IParentAggregatePayloadCommon<TParentAggregatePayload> : IAggregatePayload, IApplicableAggregatePayload<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon
{
}
// ReSharper disable once UnusedTypeParameter
public interface IParentAggregatePayload<TParentAggregatePayload, IFirstAggregatePayload> : IParentAggregatePayloadCommon<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon where IFirstAggregatePayload : IAggregatePayloadCommon, new()
{
}

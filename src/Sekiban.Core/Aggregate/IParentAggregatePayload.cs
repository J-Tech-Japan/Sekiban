namespace Sekiban.Core.Aggregate;

public interface IParentAggregatePayload<TParentAggregatePayload> : IParentAggregatePayload<TParentAggregatePayload, TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon, new()
{
}
public interface IParentAggregatePayloadCommon<TParentAggregatePayload> : IAggregatePayloadCommon, IApplicableAggregatePayload<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon
{
}
public interface IParentAggregatePayload<TParentAggregatePayload, IFirstAggregatePayload> : IParentAggregatePayloadCommon<TParentAggregatePayload>
    where TParentAggregatePayload : IAggregatePayloadCommon
    where IFirstAggregatePayload : IAggregatePayloadCommon, new()
{
}

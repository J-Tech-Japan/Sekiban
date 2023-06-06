namespace Sekiban.Core.Aggregate;

public interface IApplicableAggregatePayload<TParentAggregatePayload> : IAggregatePayloadCommon
    where TParentAggregatePayload : IAggregatePayloadCommon
{
}

namespace Sekiban.Core.Aggregate;

// ReSharper disable once UnusedTypeParameter
public interface IApplicableAggregatePayload<TParentAggregatePayload> : IAggregatePayloadCommon
    where TParentAggregatePayload : IAggregatePayloadCommon
{
}

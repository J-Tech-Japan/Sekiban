namespace Sekiban.Core.Aggregate;

public interface IAggregatePayloadGeneratable<TAggregatePayload> : IAggregatePayloadCommon
    where TAggregatePayload : IAggregatePayloadCommon
{
    public static abstract TAggregatePayload CreateInitialPayload(TAggregatePayload? _);
}

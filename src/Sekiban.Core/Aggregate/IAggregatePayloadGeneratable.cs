namespace Sekiban.Core.Aggregate;

public interface IAggregatePayloadGeneratable<TAggregatePayload> : IAggregatePayloadCommon where TAggregatePayload : IAggregatePayloadCommonBase
{
    public static abstract TAggregatePayload CreateInitialPayload(TAggregatePayload? _);
}

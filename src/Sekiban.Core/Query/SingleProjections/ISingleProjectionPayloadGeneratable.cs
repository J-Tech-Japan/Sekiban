using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionPayloadGeneratable<TSingleProjectionPayload> : IAggregatePayloadCommon
    where TSingleProjectionPayload : IAggregatePayloadCommon
{
    public static abstract TSingleProjectionPayload CreateInitialPayload();
}

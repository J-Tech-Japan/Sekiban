using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionPayloadGeneratable<TSingleProjectionPayload> : IAggregatePayloadCommon
    where TSingleProjectionPayload : IAggregatePayloadCommonBase
{
    public static abstract TSingleProjectionPayload CreateInitialPayload();
}

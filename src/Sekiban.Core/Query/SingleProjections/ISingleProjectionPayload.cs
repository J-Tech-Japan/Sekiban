using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.SingleProjections;

public interface
    ISingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload> : ISingleProjectionEventApplicable<
        TSingleProjectionPayload>
    where TAggregatePayload : IAggregatePayload
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
{
}

using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;

namespace Sekiban.Core.Query.SingleProjections;

public interface
    ISingleProjectionPayload<TAggregatePayload, TSingleProjectionPayload> : ISingleProjectionEventApplicable<
        TSingleProjectionPayload>
    where TAggregatePayload : IAggregatePayload
    where TSingleProjectionPayload : ISingleProjectionPayloadCommon, new()
{
    public Func<TSingleProjectionPayload, TSingleProjectionPayload>? GetApplyEventFunc(IEvent ev,
        IEventPayloadCommon eventPayload);
}

using Sekiban.Core.Aggregate;
using Sekiban.Core.Event;

namespace Sekiban.Core.Query.SingleProjections;

public abstract record
    SingleProjectionPayloadBase<TAggregatePayload, TSingleProjectionPayload> : ISingleProjectionEventApplicable<
        TSingleProjectionPayload>
    where TAggregatePayload : IAggregatePayload
    where TSingleProjectionPayload : ISingleProjectionPayload, new()
{
    public abstract Func<TSingleProjectionPayload, TSingleProjectionPayload>? GetApplyEventFunc(IEvent ev,
        IEventPayloadCommon eventPayload);
}

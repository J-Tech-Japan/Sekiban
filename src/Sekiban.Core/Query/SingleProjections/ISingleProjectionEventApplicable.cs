using Sekiban.Core.Event;

namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayload
    where TProjectionPayload : ISingleProjectionPayload
{
    Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(
        IEvent ev,
        IEventPayloadCommon eventPayload);
}

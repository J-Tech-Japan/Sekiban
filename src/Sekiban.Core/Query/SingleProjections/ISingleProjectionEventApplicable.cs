using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayloadCommon
    where TProjectionPayload : ISingleProjectionPayloadCommon
{
    Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(
        IEvent ev,
        IEventPayloadCommon eventPayload);
}

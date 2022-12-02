using Sekiban.Core.Event;

namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public IList<string> TargetAggregateNames()
    {
        return new List<string>();
    }

    public Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(IEvent ev,
        IEventPayloadCommon eventPayload);
}

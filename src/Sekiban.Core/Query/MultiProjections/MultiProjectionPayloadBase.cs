using Sekiban.Core.Event;

namespace Sekiban.Core.Query.MultiProjections;

public abstract record MultiProjectionPayloadBase<TProjectionPayload> : IMultiProjectionPayload
    where TProjectionPayload : IMultiProjectionPayload
{
    public virtual IList<string> TargetAggregateNames()
    {
        return new List<string>();
    }

    public abstract Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(IEvent ev,
        IEventPayloadCommon eventPayload);
}

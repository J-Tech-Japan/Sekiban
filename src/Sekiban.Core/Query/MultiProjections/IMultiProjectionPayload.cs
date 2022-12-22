using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public IList<string> TargetAggregateNames() => new List<string>();

    public Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(
        IEvent ev,
        IEventPayloadCommon eventPayload);
}

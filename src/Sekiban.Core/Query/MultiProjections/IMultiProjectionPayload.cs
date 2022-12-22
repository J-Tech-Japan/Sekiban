using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() => new();

    public Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc(
        IEvent ev,
        IEventPayloadCommon eventPayload);
}

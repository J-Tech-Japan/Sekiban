using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjector<TProjectionPayload> : IProjection where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public bool EventShouldBeApplied(IEvent ev);
    void ApplyEvent(IEvent ev);
    MultiProjectionState<TProjectionPayload> ToState();
    void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot);

    /// <summary>
    ///     対象のAggregate名リスト
    ///     Emptyの場合は、全ての集約を対象とする
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}

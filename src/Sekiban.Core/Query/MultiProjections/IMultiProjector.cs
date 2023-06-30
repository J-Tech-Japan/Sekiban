using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjector<TProjectionPayload> : IMultiProjectionCommon where TProjectionPayload : IMultiProjectionPayloadCommon, new()
{
    public bool EventShouldBeApplied(IEvent ev);
    void ApplyEvent(IEvent ev);
    MultiProjectionState<TProjectionPayload> ToState();
    void ApplySnapshot(MultiProjectionState<TProjectionPayload> snapshot);

    /// <summary>
    ///     Target Aggregate Name List
    ///     If Empty, all aggregates will be targeted.
    /// </summary>
    /// <returns></returns>
    IList<string> TargetAggregateNames();
}
